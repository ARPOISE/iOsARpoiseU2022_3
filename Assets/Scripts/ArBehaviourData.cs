/*
ArBehaviourData.cs - MonoBehaviour for ARpoise data handling.

Copyright (C) 2018, Tamiko Thiel and Peter Graf - All Rights Reserved

ARpoise - Augmented Reality point of interest service environment 

This file is part of ARpoise.

    ARpoise is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    ARpoise is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with ARpoise.  If not, see <https://www.gnu.org/licenses/>.

For more information on 

Tamiko Thiel, see www.TamikoThiel.com/
Peter Graf, see www.mission-base.com/peter/
ARpoise, see www.ARpoise.com/

*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARSubsystems;

namespace com.arpoise.arpoiseapp
{
    public interface IActivity
    {
        void Execute();
    }

    public class ArpoiseCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            return true;
        }
    }

    public class RefreshRequest
    {
        public static object ReloadLayerData = null;
        public string url;
        public string layerName;
        public float? latitude;
        public float? longitude;
    }

    public class HeaderSetActiveActivity : IActivity
    {
        public ArBehaviourData ArBehaviour;
        public string LayerTitle;

        public void Execute()
        {
            ArBehaviour.SetHeaderActive(LayerTitle);
        }
    }

    public class MenuButtonClickActivity : IActivity
    {
        public ArBehaviourData ArBehaviour;

        public void Execute()
        {
            ArBehaviour.HandleMenuButtonClick();
        }
    }

    public class MenuButtonSetActiveActivity : IActivity
    {
        public ArBehaviourData ArBehaviour;
        public List<ArLayer> Layers;

        public void Execute()
        {
            ArBehaviour.SetMenuButtonActive(Layers);
        }
    }

    public class UploadRequest
    {
        public string url;
        public byte[] data;
    }

    public class ArBehaviourData : ArBehaviourArObject
    {
        #region Constants
        public const string ArpoiseDirectoryLayer = "Arpoise-Directory";
        public const string ArvosDirectoryLayer = "AR-vos-Directory";
        public const string TT_ArpoiseDirectoryUrl = "www.tamikothiel.com/cs-bin/ArpoiseDirectory.cgi";

        #endregion

        #region Globals
        public string ArpoiseDirectoryUrl = "www.arpoise.com/cgi-bin/ArpoiseDirectory.cgi";

        #endregion

        #region Protecteds
        public bool IsSlam { get; private set; }
        public bool IsHumanBody { get; private set; }
        public bool IsCrystal { get; private set; }
        protected readonly List<ArItem> LayerItemList = new List<ArItem>();
        protected bool IsNewLayer = false;
        protected bool? MenuEnabled = null;
        protected MenuButtonSetActiveActivity MenuButtonSetActive;
        protected HeaderSetActiveActivity HeaderSetActive;
        protected MenuButtonClickActivity MenuButtonClick;
        #endregion

        #region GetData
        // A coroutine retrieving the objects
        protected override IEnumerator GetData()
        {
            long count = 0;
            string layerName = ArpoiseDirectoryLayer;
#if TT_AR_U2022_3
            ArpoiseDirectoryUrl = TT_ArpoiseDirectoryUrl;
#endif
            string uri = ArpoiseDirectoryUrl;

            bool setError = true;

            while (OriginalLatitude == 0.0 && OriginalLongitude == 0.0)
            {
                // wait for the position to be determined
                yield return new WaitForSeconds(.01f);
            }

            while (InfoPanelIsActive())
            {
                yield return new WaitForSeconds(.01f);
            }

            float startLatitude = UsedLatitude;
            float startLongitude = UsedLongitude;

            while (string.IsNullOrWhiteSpace(ErrorMessage))
            {
                MenuEnabled = null;
                count++;

                float filteredLatitude = FilteredLatitude;
                float filteredLongitude = FilteredLongitude;
                float usedLatitude = UsedLatitude;
                float usedLongitude = UsedLongitude;
                var layers = new List<ArLayer>();
                var nextPageKey = string.Empty;

                IsCrystal = IsHumanBody = IsSlam = false;
                ApplicationSleepStartMinute = -1;
                ApplicationSleepEndMinute = -1;
                AllowTakeScreenshot = -1;

                #region Download all pages of the layer

                var layerWebUrl = LayerWebUrl;
                LayerWebUrl = null;
                for (; ; )
                {
                    var url = uri + "?lang=en&version=1&radius=1500&accuracy=100"
                        + "&lat=" + usedLatitude.ToString("F6", CultureInfo.InvariantCulture)
                        + "&lon=" + usedLongitude.ToString("F6", CultureInfo.InvariantCulture)
                        + (filteredLatitude != usedLatitude ? "&latOfDevice=" + filteredLatitude.ToString("F6", CultureInfo.InvariantCulture) : string.Empty)
                        + (filteredLongitude != usedLongitude ? "&lonOfDevice=" + filteredLongitude.ToString("F6", CultureInfo.InvariantCulture) : string.Empty)
                        + "&layerName=" + layerName
                        + (!string.IsNullOrWhiteSpace(nextPageKey) ? "&pageKey=" + nextPageKey : string.Empty)
                        + "&userId=" + SystemInfo.deviceUniqueIdentifier
                        + "&client=" + ApplicationName
                        + "&bundle=" + Bundle
#if TT_AR_U2022_3
                        + "&app=TT_AR"
#endif
                        + "&os=" + OperatingSystem
                        + "&count=" + count
                    ;

                    url = FixUrl(url);
                    var request = UnityWebRequest.Get(url);
                    request.certificateHandler = new ArpoiseCertificateHandler();
                    request.timeout = 30;
                    yield return request.SendWebRequest();

                    var maxWait = request.timeout * 100;
                    while (request.result != UnityWebRequest.Result.Success
                        && request.result != UnityWebRequest.Result.ConnectionError
                        && request.result != UnityWebRequest.Result.ProtocolError
                        && request.result != UnityWebRequest.Result.DataProcessingError
                        && !request.isDone && maxWait > 0)
                    {
                        yield return new WaitForSeconds(.01f);
                        maxWait--;
                    }

                    if (maxWait < 1)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Layer '{layerName}' download timeout.";
                            yield break;
                        }
                        break;
                    }
                    if (request.result == UnityWebRequest.Result.ConnectionError
                        || request.result == UnityWebRequest.Result.ProtocolError
                        || request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Layer '{layerName}' {request.result}, {request.error}";
                            yield break;
                        }
                        break;
                    }

                    var text = request.downloadHandler.text;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Layer '{layerName}', empty text.";
                            yield break;
                        }
                        break;
                    }
                    try
                    {
                        var layer = ArLayer.Create(text);
                        if (!string.IsNullOrWhiteSpace(layer.redirectionUrl))
                        {
                            uri = layer.redirectionUrl.Trim();
                        }
                        if (!string.IsNullOrWhiteSpace(layer.redirectionLayer))
                        {
                            layerName = layer.redirectionLayer.Trim();
                        }
                        if (!string.IsNullOrWhiteSpace(layer.redirectionUrl) || !string.IsNullOrWhiteSpace(layer.redirectionLayer))
                        {
                            layers.Clear();
                            nextPageKey = string.Empty;
                            //Debug.Log("Redirected to " + layer.redirectionUrl);
                            continue;
                        }
                        layers.Add(layer);
                        if (layer.morePages == false || string.IsNullOrWhiteSpace(layer.nextPageKey))
                        {
                            LayerWebUrl = uri + "?layerName=" + layerName;
                            //Debug.Log("Loaded Layer " + layerName);
                            break;
                        }
                        nextPageKey = layer.nextPageKey;
                    }
                    catch (Exception e)
                    {
                        if (setError)
                        {
                            ErrorMessage = "Layer '" + layerName + "' parse error: " + e.Message;
                            yield break;
                        }
                        break;
                    }
                }
                #endregion

                #region Handle the showMenuButton of the layers
                MenuButtonSetActive = new MenuButtonSetActiveActivity { ArBehaviour = this, Layers = layers.ToList() };
                #endregion

                #region Download the asset bundle for icons
                var assetBundleUrls = new Dictionary<string, int>();
                var iconAssetBundleUrl = "www.arpoise.com/AB/U2022arpoiseicons.ace";
                assetBundleUrls[iconAssetBundleUrl] = -1;
                uint versionOfTheDay = 0;
                if (int.TryParse(DateTime.Today.ToString("yyMMdd"), out var value))
                {
                    versionOfTheDay = (uint)value * 100;
                }
                foreach (var url in assetBundleUrls)
                {
                    if (AssetBundles.ContainsKey(url.Key))
                    {
                        continue;
                    }
                    var assetBundleUri = FixUrl(GetAssetBundleUrl(url.Key));
                    var request = UnityWebRequestAssetBundle.GetAssetBundle(assetBundleUri, versionOfTheDay, 0);
                    request.certificateHandler = new ArpoiseCertificateHandler();
                    request.timeout = 60;
                    yield return request.SendWebRequest();

                    var maxWait = request.timeout * 100;
                    while (request.result != UnityWebRequest.Result.Success
                        && request.result != UnityWebRequest.Result.ConnectionError
                        && request.result != UnityWebRequest.Result.ProtocolError
                        && request.result != UnityWebRequest.Result.DataProcessingError
                        && !request.isDone && maxWait > 0)
                    {
                        yield return new WaitForSeconds(.01f);
                        maxWait--;
                    }

                    if (maxWait < 1)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Icons '{assetBundleUri}' download timeout.";
                            yield break;
                        }
                        continue;
                    }
                    if (request.result == UnityWebRequest.Result.ConnectionError
                        || request.result == UnityWebRequest.Result.ProtocolError
                        || request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Icons '{assetBundleUri}' {request.result}, {request.error}";
                            yield break;
                        }
                        break;
                    }

                    var assetBundle = DownloadHandlerAssetBundle.GetContent(request);
                    if (assetBundle == null)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Icons '{assetBundleUri}' is null.";
                            yield break;
                        }
                        continue;
                    }
                    AssetBundles[url.Key] = assetBundle;
                }
                #endregion

                #region Handle lists of possible layers to show
                {
                    var itemList = new List<ArItem>();
                    foreach (var layer in layers.Where(x => x.hotspots != null))
                    {
                        if (ArpoiseDirectoryLayer.Equals(layer.layer) || ArvosDirectoryLayer.Equals(layer.layer))
                        {
                            foreach (var poi in layer.hotspots)
                            {
                                GameObject spriteObject = null;
                                var spriteName = poi.line4;
                                if (!string.IsNullOrWhiteSpace(spriteName))
                                {
                                    AssetBundle iconAssetBundle = null;
                                    if (AssetBundles.TryGetValue(iconAssetBundleUrl, out iconAssetBundle))
                                    {
                                        spriteObject = iconAssetBundle.LoadAsset<GameObject>(spriteName);
                                    }
                                }
                                var sprite = spriteObject != null ? spriteObject.GetComponent<SpriteRenderer>().sprite : (Sprite)null;

                                itemList.Add(new ArItem
                                {
                                    layerName = poi.title,
                                    itemName = poi.line1,
                                    line2 = poi.line2,
                                    line3 = poi.line3,
                                    url = poi.BaseUrl,
                                    distance = (int)poi.distance,
                                    icon = sprite
                                });
                            }
                        }
                    }

                    if (itemList.Any())
                    {
                        LayerItemList.Clear();
                        LayerItemList.AddRange(itemList);

                        // There are different layers to show
                        MenuButtonClick = new MenuButtonClickActivity { ArBehaviour = this };

                        // Wait for the user to select a layer
                        for (; ; )
                        {
                            var refreshRequest = RefreshRequest;
                            RefreshRequest = null;
                            if (refreshRequest != null)
                            {
                                //Debug.Log("RefreshRequest " + refreshRequest.layerName + ", " + refreshRequest.url);
                                count = 0;
                                layerName = refreshRequest.layerName;
                                uri = refreshRequest.url;
                                FixedDeviceLatitude = refreshRequest.latitude;
                                FixedDeviceLongitude = refreshRequest.longitude;
                                break;
                            }
                            yield return new WaitForSeconds(.1f);
                        }
                        continue;
                    }
                }
                #endregion

                #region Download all inner layers
                var innerLayers = new Dictionary<string, bool>();
                foreach (var layer in layers.Where(x => x.hotspots != null))
                {
                    foreach (var hotspot in layer.hotspots.Where(x => !string.IsNullOrWhiteSpace(x.InnerLayerName)))
                    {
                        if (!innerLayers.ContainsKey(hotspot.InnerLayerName))
                        {
                            innerLayers[hotspot.InnerLayerName] = layer.isDefaultLayer;
                        }
                    }
                }

                string innerLayer;
                while ((innerLayer = innerLayers.Keys.FirstOrDefault()) != null)
                {
                    if (InnerLayers.ContainsKey(innerLayer))
                    {
                        innerLayers.Remove(innerLayer);
                        continue;
                    }

                    if (layerName.Equals(innerLayer))
                    {
                        InnerLayers[layerName] = layers;
                        continue;
                    }

                    var isDefaultLayer = innerLayers[innerLayer];
                    var latitude = isDefaultLayer ? 0f : usedLatitude;
                    var longitude = isDefaultLayer ? 0f : usedLongitude;
                    nextPageKey = string.Empty;
                    for (; ; )
                    {
                        var url = uri + "?lang=en&version=1&radius=1500&accuracy=100&innerLayer=true"
                        + "&lat=" + latitude.ToString("F6", CultureInfo.InvariantCulture)
                        + "&lon=" + longitude.ToString("F6", CultureInfo.InvariantCulture)
                        + ((filteredLatitude != latitude) ? "&latOfDevice=" + filteredLatitude.ToString("F6", CultureInfo.InvariantCulture) : string.Empty)
                        + ((filteredLongitude != longitude) ? "&lonOfDevice=" + filteredLongitude.ToString("F6", CultureInfo.InvariantCulture) : string.Empty)
                        + "&layerName=" + innerLayer
                        + (!string.IsNullOrWhiteSpace(nextPageKey) ? "&pageKey=" + nextPageKey : string.Empty)
                        + "&userId=" + SystemInfo.deviceUniqueIdentifier
                        + "&client=" + ApplicationName
                        + "&bundle=" + Bundle
                        + "&os=" + OperatingSystem
                        ;

                        url = FixUrl(url);
                        var request = UnityWebRequest.Get(url);
                        request.certificateHandler = new ArpoiseCertificateHandler();
                        request.timeout = 30;
                        yield return request.SendWebRequest();

                        var maxWait = request.timeout * 100;
                        while (request.result != UnityWebRequest.Result.Success
                            && request.result != UnityWebRequest.Result.ConnectionError
                            && request.result != UnityWebRequest.Result.ProtocolError
                            && request.result != UnityWebRequest.Result.DataProcessingError
                            && !request.isDone && maxWait > 0)
                        {
                            yield return new WaitForSeconds(.01f);
                            maxWait--;
                        }

                        if (maxWait < 1)
                        {
                            if (setError)
                            {
                                ErrorMessage = $"Layer '{innerLayer}' download timeout.";
                                yield break;
                            }
                            break;
                        }
                        if (request.result == UnityWebRequest.Result.ConnectionError
                            || request.result == UnityWebRequest.Result.ProtocolError
                            || request.result == UnityWebRequest.Result.DataProcessingError)
                        {
                            if (setError)
                            {
                                ErrorMessage = $"Layer '{innerLayer}' {request.result}, {request.error}";
                                yield break;
                            }
                            break;
                        }

                        var text = request.downloadHandler.text;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            if (setError)
                            {
                                ErrorMessage = $"Layer '{innerLayer}', empty text.";
                                yield break;
                            }
                            break;
                        }

                        try
                        {
                            var layer = ArLayer.Create(text);
                            if (layer != null)
                            {
                                foreach (var hotspot in layer.hotspots.Where(x => !string.IsNullOrWhiteSpace(x.InnerLayerName)))
                                {
                                    if (!innerLayers.ContainsKey(hotspot.InnerLayerName))
                                    {
                                        innerLayers[hotspot.InnerLayerName] = layer.isDefaultLayer;
                                    }
                                }
                            }

                            List<ArLayer> layersList = null;
                            if (InnerLayers.TryGetValue(innerLayer, out layersList))
                            {
                                layersList.Add(layer);
                            }
                            else
                            {
                                InnerLayers[innerLayer] = new List<ArLayer> { layer };
                            }

                            if (layer.morePages == false || string.IsNullOrWhiteSpace(layer.nextPageKey))
                            {
                                break;
                            }
                            nextPageKey = layer.nextPageKey;
                        }
                        catch (Exception e)
                        {
                            if (setError)
                            {
                                ErrorMessage = "Layer " + innerLayer + " parse error: " + e.Message;
                                yield break;
                            }
                            break;
                        }
                    }
                }
                #endregion

                #region Download all asset bundles
                foreach (var layer in layers.Where(x => x.hotspots != null))
                {
                    foreach (var hotspot in layer.hotspots.Where(x => !string.IsNullOrWhiteSpace(x.BaseUrl)))
                    {
                        hotspot.ArLayer ??= layer;
                        if (assetBundleUrls.TryGetValue(hotspot.BaseUrl, out var version))
                        {
                            if (version > 0)
                            {
                                if (hotspot.AssetBundleCacheVersion == 0)
                                {
                                    assetBundleUrls[hotspot.BaseUrl] = 0;
                                }
                                else if (hotspot.AssetBundleCacheVersion > version)
                                {
                                    assetBundleUrls[hotspot.BaseUrl] = hotspot.AssetBundleCacheVersion;
                                }
                            }
                        }
                        else
                        {
                            assetBundleUrls[hotspot.BaseUrl] = hotspot.AssetBundleCacheVersion;
                        }
                    }
                }

                foreach (var layerList in InnerLayers.Values)
                {
                    foreach (var layer in layerList.Where(x => x.hotspots != null))
                    {
                        foreach (var hotspot in layer.hotspots.Where(x => !string.IsNullOrWhiteSpace(x.BaseUrl)))
                        {
                            hotspot.ArLayer ??= layer;
                            if (assetBundleUrls.TryGetValue(hotspot.BaseUrl, out var version))
                            {
                                if (version > 0)
                                {
                                    if (hotspot.AssetBundleCacheVersion == 0)
                                    {
                                        assetBundleUrls[hotspot.BaseUrl] = 0;
                                    }
                                    else if (hotspot.AssetBundleCacheVersion > version)
                                    {
                                        assetBundleUrls[hotspot.BaseUrl] = hotspot.AssetBundleCacheVersion;
                                    }
                                }
                            }
                            else
                            {
                                assetBundleUrls[hotspot.BaseUrl] = hotspot.AssetBundleCacheVersion;
                            }
                        }
                    }
                }

                for (int i = 0; i < 3; i++)
                {
                    var bundleWebRequests = new List<Tuple<string, string, UnityWebRequest>>();
                    if (i > 0)
                    {
                        yield return new WaitForSeconds(3);
                    }
                    foreach (var url in assetBundleUrls)
                    {
                        if (AssetBundles.ContainsKey(url.Key))
                        {
                            continue;
                        }
                        var assetBundleUri = FixUrl(GetAssetBundleUrl(url.Key));
                        UnityWebRequest request;
                        if (url.Value <= 0)
                        {
                            request = UnityWebRequestAssetBundle.GetAssetBundle(assetBundleUri, 0);
                        }
                        else
                        {
                            request = UnityWebRequestAssetBundle.GetAssetBundle(assetBundleUri, (uint)url.Value, 0);
                        }
                        request.certificateHandler = new ArpoiseCertificateHandler();
                        request.timeout = 60;
                        bundleWebRequests.Add(new Tuple<string, string, UnityWebRequest>(url.Key, assetBundleUri, request));
                        yield return request.SendWebRequest();
                    }

                    foreach (var tuple in bundleWebRequests)
                    {
                        var url = tuple.Item1;
                        var assetBundleUri = tuple.Item2;
                        var request = tuple.Item3;

                        var maxWait = request.timeout * 100;
                        while (request.result != UnityWebRequest.Result.Success
                            && request.result != UnityWebRequest.Result.ConnectionError
                            && request.result != UnityWebRequest.Result.ProtocolError
                            && request.result != UnityWebRequest.Result.DataProcessingError
                            && !request.isDone && maxWait > 0)
                        {
                            yield return new WaitForSeconds(.01f);
                            maxWait--;
                        }

                        if (maxWait < 1)
                        {
                            if (setError)
                            {
                                ErrorMessage = $"Bundle '{assetBundleUri}' download timeout.";
                            }
                            break;
                        }
                        if (request.result == UnityWebRequest.Result.ConnectionError
                            || request.result == UnityWebRequest.Result.ProtocolError
                            || request.result == UnityWebRequest.Result.DataProcessingError)
                        {
                            if (setError)
                            {
                                ErrorMessage = $"Bundle '{assetBundleUri}' {request.result}, {request.error}";
                            }
                            break;
                        }

                        var assetBundle = DownloadHandlerAssetBundle.GetContent(request);
                        if (assetBundle == null)
                        {
                            if (setError)
                            {
                                ErrorMessage = $"Bundle '{assetBundleUri}' is null.";
                            }
                            break;
                        }
                        AssetBundles[url] = assetBundle;
                    }
                    if (string.IsNullOrWhiteSpace(ErrorMessage))
                    {
                        break;
                    }
                    if (i == 2)
                    {
                        yield break;
                    }
                    ErrorMessage = string.Empty;
                }
                #endregion

                #region Download the trigger images
                var triggerImageUrls = new HashSet<string>();

                foreach (var layer in layers.Where(x => x.hotspots != null))
                {
                    triggerImageUrls.UnionWith(layer.hotspots.Where(x => !string.IsNullOrWhiteSpace(x.TriggerImageURL)).Select(x => x.TriggerImageURL));
                }

                foreach (var layerList in InnerLayers.Values)
                {
                    foreach (var layer in layerList.Where(x => x.hotspots != null))
                    {
                        triggerImageUrls.UnionWith(layer.hotspots.Where(x => !string.IsNullOrWhiteSpace(x.TriggerImageURL)).Select(x => x.TriggerImageURL));
                    }
                }

                var webRequests = new List<Tuple<string, string, UnityWebRequest>>();

                if (triggerImageUrls.Any(x => IsSlamUrl(x)))
                {
                    IsSlam = true;
                    IsCrystal = false;
                    IsHumanBody = false;
                    triggerImageUrls.Clear();
                }
                else if (triggerImageUrls.Any(x => IsHumanBodyUrl(x)))
                {
                    IsHumanBody = true;
                    IsCrystal = false;
                    IsSlam = false;
                    triggerImageUrls.Clear();
                }
                else if (triggerImageUrls.Any(x => IsCrystalUrl(x)))
                {
                    IsCrystal = true;
                    IsHumanBody = false;
                    IsSlam = false;
                }
                if (!IsSlam || LayerWebUrl != layerWebUrl)
                {
                    SlamObjects.Clear();
                }
                if (!IsHumanBody || LayerWebUrl != layerWebUrl)
                {
                    HumanBodyObjects.Clear();
                }
                if (!IsCrystal || LayerWebUrl != layerWebUrl)
                {
                    CrystalObjects.Clear();
                }
                foreach (var url in triggerImageUrls.Where(x => !IsCrystalUrl(x)))
                {
                    if (TriggerImages.ContainsKey(url))
                    {
                        continue;
                    }
                    var triggerImageUri = FixUrl(url);
                    var request = UnityWebRequestTexture.GetTexture(triggerImageUri);
                    request.certificateHandler = new ArpoiseCertificateHandler();
                    request.timeout = 30;
                    webRequests.Add(new Tuple<string, string, UnityWebRequest>(url, triggerImageUri, request));
                    yield return request.SendWebRequest();
                }

                foreach (var tuple in webRequests)
                {
                    var url = tuple.Item1;
                    var triggerImageUri = tuple.Item2;
                    var request = tuple.Item3;

                    var maxWait = request.timeout * 100;
                    while (request.result != UnityWebRequest.Result.Success
                        && request.result != UnityWebRequest.Result.ConnectionError
                        && request.result != UnityWebRequest.Result.ProtocolError
                        && request.result != UnityWebRequest.Result.DataProcessingError
                        && !request.isDone && maxWait > 0)
                    {
                        yield return new WaitForSeconds(.01f);
                        maxWait--;
                    }

                    if (maxWait < 1)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Image '{triggerImageUri}' download timeout.";
                            yield break;
                        }
                        continue;
                    }
                    if (request.result == UnityWebRequest.Result.ConnectionError
                        || request.result == UnityWebRequest.Result.ProtocolError
                        || request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Image '{triggerImageUri}' {request.result}, {request.error}";
                            yield break;
                        }
                        break;
                    }

                    var texture = DownloadHandlerTexture.GetContent(request);
                    if (texture == null)
                    {
                        if (setError)
                        {
                            ErrorMessage = $"Image '{triggerImageUri}', empty texture.";
                            yield break;
                        }
                        continue;
                    }
                    TriggerImages[url] = texture;
                }
                #endregion

                #region Activate the header
                var layerTitle = layers.Select(x => x.layerTitle).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                HeaderSetActive = new HeaderSetActiveActivity { LayerTitle = layerTitle, ArBehaviour = this };
                #endregion

                #region Set the remote server url
                var layerWithRemoteServerUrl = layers.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.RemoteServerUrl));
                if (layerWithRemoteServerUrl != null)
                {
                    SetRemoteServerUrl(layerWithRemoteServerUrl.RemoteServerUrl, layerWithRemoteServerUrl.SceneUrl, layerTitle, null);
                }
                else
                {
                    SetRemoteServerUrl(null, null, null, null);
                }
                #endregion

                var layerWithLightRange = layers.FirstOrDefault(x => x.LightRange.HasValue);
                if (layerWithLightRange != null)
                {
                    LightRange = layerWithLightRange.LightRange.Value;
                }
                else
                {
                    LightRange = null;
                }

                #region Set the lights
                var lightGameObject = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(x => x.name == "Directional Light N");
                var light = lightGameObject != null ? lightGameObject.GetComponent<Light>() : null;
                if (light != null)
                {
                    light.intensity = 1;
                    var layer = layers.FirstOrDefault(x => x.DirectionalLightN_Intensity >= 0);
                    if (layer != null)
                    {
                        light.intensity = layer.DirectionalLightN_Intensity;
                    }

                    layer = layers.FirstOrDefault(x => x.DirectionalLightN_IsActive == false);
                    if (layer != null)
                    {
                        lightGameObject.SetActive(false);
                    }
                    else
                    {
                        lightGameObject.SetActive(true);
                    }
                }

                lightGameObject = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(x => x.name == "Directional Light SEE");
                light = lightGameObject != null ? lightGameObject.GetComponent<Light>() : null;
                if (light != null)
                {
                    light.intensity = 1;
                    var layer = layers.FirstOrDefault(x => x.DirectionalLightSEE_Intensity >= 0);
                    if (layer != null)
                    {
                        light.intensity = layer.DirectionalLightSEE_Intensity;
                    }

                    layer = layers.FirstOrDefault(x => x.DirectionalLightSEE_IsActive == false);
                    if (layer != null)
                    {
                        lightGameObject.SetActive(false);
                    }
                    else
                    {
                        lightGameObject.SetActive(true);
                    }
                }

                lightGameObject = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(x => x.name == "Directional Light SWW");
                light = lightGameObject != null ? lightGameObject.GetComponent<Light>() : null;
                if (light != null)
                {
                    light.intensity = 1;
                    var layer = layers.FirstOrDefault(x => x.DirectionalLightSWW_Intensity >= 0);
                    if (layer != null)
                    {
                        light.intensity = layer.DirectionalLightSWW_Intensity;
                    }

                    layer = layers.FirstOrDefault(x => x.DirectionalLightSWW_IsActive == false);
                    if (layer != null)
                    {
                        lightGameObject.SetActive(false);
                    }
                    else
                    {
                        lightGameObject.SetActive(true);
                    }
                }
                #endregion

                #region Create or handle the object state
                List<ArObject> existingArObjects = null;
                var arObjectState = ArObjectState;
                if (arObjectState != null)
                {
                    existingArObjects = arObjectState.ArObjects.ToList();
                }
                arObjectState = CreateArObjectState(existingArObjects, layers);
                setError = false;

                if (ArObjectState == null)
                {
                    ErrorMessage = CreateArObjects(arObjectState, null, SceneAnchor.transform, arObjectState.ArPois);
                    arObjectState.ArPois.Clear();

                    if (!string.IsNullOrWhiteSpace(ErrorMessage))
                    {
                        yield break;
                    }
                    if (!arObjectState.ArObjects.Any() && !IsCrystal && !IsHumanBody && !IsSlam && !HasTriggerImages)
                    {
                        var message = layers.Select(x => x.noPoisMessage).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                        if (string.IsNullOrWhiteSpace(message))
                        {
                            message = "Sorry, there are no augments at your location!";
                        }
                        ErrorMessage = message;
                        yield break;
                    }
                    arObjectState.SetArObjectsToPlace();

                    StartTicks = DateTime.Now.Ticks;
                    ArObjectState = arObjectState;
                }
                else
                {
                    if (arObjectState.ArPois.Any())
                    {
                        ArObjectState.ArPois.AddRange(arObjectState.ArPois);
                    }
                    if (arObjectState.ArObjectsToDelete.Any())
                    {
                        ArObjectState.ArObjectsToDelete.AddRange(arObjectState.ArObjectsToDelete);
                    }
                    ArObjectState.IsDirty = true;
                }
                IsNewLayer = true;
                #endregion

                #region Wait for refresh
                var refreshInterval = RefreshInterval;
                var doNotRefresh = refreshInterval < 1;

                long nowTicks = DateTime.Now.Ticks;
                long waitUntil = nowTicks + (long)refreshInterval * TimeSpan.TicksPerSecond;

                while (doNotRefresh || nowTicks < waitUntil)
                {
                    nowTicks = DateTime.Now.Ticks;

                    var refreshRequest = RefreshRequest;
                    RefreshRequest = null;

                    var refreshDistance = RefreshDistance;
                    if (refreshRequest == null && refreshDistance > 0)
                    {
                        var distance = CalculateDistance(startLatitude, startLongitude, UsedLatitude, UsedLongitude);
                        if (distance > refreshDistance)
                        {
                            startLatitude = UsedLatitude;
                            startLongitude = UsedLongitude;
                            refreshRequest = new RefreshRequest
                            {
                                url = ArpoiseDirectoryUrl,
                                layerName = ArpoiseDirectoryLayer
                            };
                        }
                    }
                    if (refreshRequest != null)
                    {
                        //Debug.Log("RefreshRequest " + refreshRequest.layerName + ", " + refreshRequest.url);
                        count = 0;
                        if (nameof(RefreshRequest.ReloadLayerData).Equals(refreshRequest.layerName))
                        {
                            var objectState = ArObjectState;
                            if (objectState != null)
                            {
                                objectState = CreateArObjectState(objectState.ArObjects.ToList(), new List<ArLayer>());
                                if (objectState.ArObjectsToDelete.Any())
                                {
                                    ArObjectState.ArObjectsToDelete.AddRange(objectState.ArObjectsToDelete);
                                }
                                ArObjectState.IsDirty = true;
                            }
                            InnerLayers.Clear();
                        }
                        else if (!string.IsNullOrWhiteSpace(refreshRequest.layerName))
                        {
                            layerName = refreshRequest.layerName;
                        }

                        if (!string.IsNullOrWhiteSpace(refreshRequest.url))
                        {
                            uri = refreshRequest.url;
                        }
                        if (refreshRequest.latitude.HasValue)
                        {
                            FixedDeviceLatitude = refreshRequest.latitude;
                        }
                        if (refreshRequest.longitude.HasValue)
                        {
                            FixedDeviceLongitude = refreshRequest.longitude;
                        }
                        ErrorMessage = string.Empty;
                        foreach (var triggerObject in TriggerObjects.Values)
                        {
                            triggerObject.isActive = false;
                        }
                        HasTriggerImages = false;
                        IsCrystal = IsHumanBody = IsSlam = false;
                        break;
                    }
                    yield return new WaitForSeconds(.1f);
                }
                #endregion
            }
            yield break;
        }
        #endregion

        #region Misc
        public virtual void SetMenuButtonActive(List<ArLayer> layers)
        {
        }

        public virtual void HandleInfoPanelClosed()
        {
        }

        public virtual void HandleMenuButtonClick()
        {
            //Debug.Log("ArBehaviourData.HandleMenuButtonClick");
        }

        public virtual void SetHeaderActive(string layerTitle)
        {
        }

        private string GetAssetBundleUrl(string url)
        {
#if UNITY_IOS
            if (url.EndsWith(".ace"))
            {
                url = url.Replace(".ace", "i.ace");
            }
            else
            {
                url += "i";
            }
#endif
            return url;
        }

        public static string FixUrl(string url)
        {
            while (url.Contains('\\'))
            {
                url = url.Replace("\\", string.Empty);
            }
            if (url.StartsWith("http://"))
            {
                url = url.Substring(7);
            }
            if (!url.StartsWith("https://"))
            {
                url = "https://" + url;
            }
            return url;
        }
        #endregion
    }
}
