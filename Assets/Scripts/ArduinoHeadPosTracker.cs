#if WindowsARpoise
using System;
using System.Globalization;
using System.IO.Ports;
using UnityEngine;

public class ArduinoHeadPosTracker : MonoBehaviour
{
    [Header("Serial")]
    public string portName = "COM6";
    public int baudRate = 115200;

    [Header("Total Gaze")]
    public float maxGazeX = 40.0f;
    public float maxGazeY = 50.0f;

    [Header("Eyes")]
    public float eyeSpeed = 20.0f;

    [Header("Head")]
    public float maxHeadX = 12.0f;
    public float maxHeadY = 16.7f;
    public float headDelay = 0.30f;
    public float headSpeed = 3.0f;

    [Header("Sensor Geometry")]
    public float sensorCenterX = 14.0f;
    public float sensorCenterY = 19.5f;
    public float sensorHalfWidth = 14.0f;
    public float sensorHalfHeight = 19.5f;

    [Header("Debug")]
    public bool connected;
    public bool hasPosition;
    public bool foundRightEye;
    public bool foundLeftEye;
    public int receivedPositions;
    public Vector3 fistPosition;
    public float horizontalInput;
    public float verticalInput;
    public float desiredGazeX;
    public float desiredGazeY;
    public float currentHeadX;
    public float currentHeadY;
    public float currentEyeX;
    public float currentEyeY;
    public Vector3 rightEyeRotation;
    public Vector3 leftEyeRotation;
    public string lastPositionLine;
    public string lastRawLine;

    private Transform RightEye;
    private Transform LeftEye;
    private SerialPort serialPort;
    private string serialBuffer = "";
    private Quaternion headNeutralRotation;
    private Quaternion rightEyeNeutralRotation;
    private Quaternion leftEyeNeutralRotation;
    private float headDelayTimer;
    private float previousDesiredGazeX;
    private float previousDesiredGazeY;

    void Start()
    {
        Debug.Log("HeadFistTracker started");
        RightEye = transform.Find("RightEye");
        LeftEye = transform.Find("LeftEye");
        foundRightEye = RightEye != null;
        foundLeftEye = LeftEye != null;

        if (RightEye == null)
        {
            Debug.LogError("Could not find child: RightEye");
        }
        else
        {
            rightEyeNeutralRotation = RightEye.localRotation;
            Debug.Log("Found RightEye");
        }

        if (LeftEye == null)
        {
            Debug.LogError("Could not find child: LeftEye");
        }
        else
        {
            leftEyeNeutralRotation = LeftEye.localRotation;
            Debug.Log("Found LeftEye");
        }

        headNeutralRotation = transform.localRotation;
        OpenSerialPort();
    }

    void OpenSerialPort()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 100;
            serialPort.WriteTimeout = 100;
            serialPort.DtrEnable = true;
            serialPort.RtsEnable = true;
            serialPort.Open();
            connected = true;
            Debug.Log("Arduino connected on " + portName + ", port IsOpen: " + serialPort.IsOpen + ", baudRate: " + baudRate + " baud");
        }
        catch (Exception e)
        {
            connected = false;
            Debug.LogError("Could not open Arduino serial port: " + e.Message);
        }
    }

    void Update()
    {
        ReadSerial();

        if (!hasPosition)
        {
            return;
        }

        CalculateDesiredGaze();
        UpdateHeadDelay();
        UpdateHead();
        UpdateEyes();
        UpdateDebugValues();
    }

    void ReadSerial()
    {
        if (serialPort == null || !serialPort.IsOpen)
        {
            return;
        }

        try
        {
            string incoming = serialPort.ReadExisting();

            if (incoming.Length == 0)
            {
                return;
            }

            serialBuffer += incoming;
            int newlineIndex = serialBuffer.IndexOf('\n');

            while (newlineIndex >= 0)
            {
                string line = serialBuffer.Substring(0, newlineIndex).Trim();
                serialBuffer = serialBuffer.Substring(newlineIndex + 1);

                if (line.Length > 0)
                {
                    lastRawLine = line;
                    ParseSerialLine(line);
                }

                newlineIndex = serialBuffer.IndexOf('\n');
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Serial read error: " + e.Message);
        }
    }

    void ParseSerialLine(string line)
    {
        if (!line.StartsWith("POS,"))
        {
            return;
        }

        string[] parts = line.Split(',');

        if (parts.Length != 4)
        {
            return;
        }

        float x;
        float y;
        float z;
        bool xOK = float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
        bool yOK = float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        bool zOK = float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z);

        if (!xOK || !yOK || !zOK)
        {
            return;
        }

        fistPosition = new Vector3(x, y, z);
        lastPositionLine = line;
        hasPosition = true;
        receivedPositions++;
    }

    void CalculateDesiredGaze()
    {
        float sensorHorizontal = Mathf.Clamp((fistPosition.x - sensorCenterX) / sensorHalfWidth, -1.0f, 1.0f);
        float sensorVertical = Mathf.Clamp((fistPosition.y - sensorCenterY) / sensorHalfHeight, -1.0f, 1.0f);

        horizontalInput = sensorHorizontal;
        verticalInput = sensorVertical;

        // Experimentally established:
        //
        // Fist up    -> local X positive
        // Fist down  -> local X negative
        //
        // Fist left  -> local Y positive
        // Fist right -> local Y negative

        desiredGazeX = sensorVertical * maxGazeX;
        desiredGazeY = -sensorHorizontal * maxGazeY;
    }

    void UpdateHeadDelay()
    {
        float xChange = Mathf.Abs(desiredGazeX - previousDesiredGazeX);
        float yChange = Mathf.Abs(desiredGazeY - previousDesiredGazeY);

        if (xChange > 2.0f || yChange > 2.0f)
        {
            headDelayTimer = headDelay;
        }

        previousDesiredGazeX = desiredGazeX;
        previousDesiredGazeY = desiredGazeY;

        if (headDelayTimer > 0.0f)
        {
            headDelayTimer -= Time.deltaTime;
        }
    }

    void UpdateHead()
    {
        float targetHeadX = verticalInput * maxHeadX;
        float targetHeadY = -horizontalInput * maxHeadY;

        if (headDelayTimer <= 0.0f)
        {
            float t = 1.0f - Mathf.Exp(-headSpeed * Time.deltaTime);
            currentHeadX = Mathf.Lerp(currentHeadX, targetHeadX, t);
            currentHeadY = Mathf.Lerp(currentHeadY, targetHeadY, t);
        }

        Quaternion targetRotation = headNeutralRotation * Quaternion.Euler(currentHeadX, currentHeadY, 0.0f);
        transform.localRotation = targetRotation;
    }

    void UpdateEyes()
    {
        // Head and eyes use the same experimentally
        // established local X/Y rotation convention.
        //
        // Therefore the eye only supplies the part of the
        // total gaze rotation that the head is not supplying.

        float targetEyeX = desiredGazeX - currentHeadX;
        float targetEyeY = desiredGazeY - currentHeadY;
        float t = 1.0f - Mathf.Exp(-eyeSpeed * Time.deltaTime);

        currentEyeX = Mathf.Lerp(currentEyeX, targetEyeX, t);
        currentEyeY = Mathf.Lerp(currentEyeY, targetEyeY, t);

        Quaternion eyeRotation = Quaternion.Euler(currentEyeX, currentEyeY, 0.0f);

        if (RightEye != null)
        {
            RightEye.localRotation = rightEyeNeutralRotation * eyeRotation;
        }

        if (LeftEye != null)
        {
            LeftEye.localRotation = leftEyeNeutralRotation * eyeRotation;
        }
    }

    void UpdateDebugValues()
    {
        if (RightEye != null)
        {
            rightEyeRotation = RightEye.localEulerAngles;
        }

        if (LeftEye != null)
        {
            leftEyeRotation = LeftEye.localEulerAngles;
        }
    }

    void OnDestroy()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Close();
        }

        connected = false;
    }
}
#endif