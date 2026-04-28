using KSPShaderTools.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Smooth.Slinq.Test;
using UniLinq;
using UnityEngine;

namespace KSPShaderTools
{

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class TexturesUnlimitedLoader : MonoBehaviour
    {

        /*  Custom Shader Loading for KSP
         *  Includes loading of platform-specific bundles, or 'universal' bundles.  
         *  Bundles to be loaded are determined by config files (KSP_SHADER_BUNDLE)
         *  Each bundle can have multiple shaders in it.
         *  
         *  Shader / Icon shaders are determined by another config node (KSP_SHADER_DATA)
         *  with a key for shader = <shaderName> and iconShader = <iconShaderName>
         *  
         *  Shaders are applied to models in the database through a third config node (KSP_MODEL_SHADER)
         *  --these specify which database-model-URL to apply a specific texture set to (KSP_TEXTURE_SET)
         *  
         *  Texture sets (KSP_TEXTURE_SET) can be referenced in the texture-switch module for run-time texture switching capability.
         *  
         *  
         *  //eve shader loading data -- need to examine what graphics APIs the SSTU shaders are set to build for -- should be able to build 'universal' bundles (done)
         *  https://github.com/WazWaz/EnvironmentalVisualEnhancements/blob/master/Assets/Editor/BuildABs.cs
         */

        #region REGION - Maps of shaders, texture sets, procedural textures

        public static Dictionary<string, Shader> loadedShaders = new Dictionary<string, Shader>();

        /// <summary>
        /// List of loaded shaders and corresponding icon shader.  Loaded from KSP_SHADER_DATA config nodes.
        /// </summary>
        public static Dictionary<string, IconShaderData> iconShaders = new Dictionary<string, IconShaderData>();

        /// <summary>
        /// List of loaded global texture sets.  Loaded from KSP_TEXTURE_SET config nodes.
        /// </summary>
        public static Dictionary<string, TextureSet> loadedTextureSets = new Dictionary<string, TextureSet>();


        public static Dictionary<string, TextureSet> loadedModelShaderSets = new Dictionary<string, TextureSet>();

        /// <summary>
        /// List of procedurally created 'solid color' textures to use for filling in empty texture slots in materials.
        /// </summary>
        public static Dictionary<string, Texture2D> textureColors = new Dictionary<string, Texture2D>();

        /// <summary>
        /// List of shaders with transparency, and the keywords that enable it.  Used to properly set the render-queue for materials.
        /// </summary>
        public static Dictionary<string, TransparentShaderData> transparentShaderData = new Dictionary<string, TransparentShaderData>();

        public static int diffuseTextureRenderQueue = 2000;

        public static int transparentTextureRenderQueue = 3000;

        #endregion ENDREGION - Maps of shaders, texture sets, procedural textures

        #region REGION - Config Values loaded from disk

        public static bool logAll = false;
        public static bool logReplacements = false;
        public static bool logErrors = false;

        public static int recolorGUIWidth = 400;
        public static int recolorGUISectionHeight = 540;
        public static int recolorGUITotalHeight = 100;

        public static bool alternateRender = false;

        public static ConfigNode configurationNode;

        #endregion ENDREGION - Config Values loaded from disk

        public static TexturesUnlimitedLoader INSTANCE;

        private static List<Action> postLoadCallbacks = new List<Action>();

        private static EventVoid.OnEvent partListLoadedEvent;

        private static GraphicsAPIGUI apiCheckGUI;

        public void Start()
        {
            Log.log("TexturesUnlimitedLoader - Start()");
            INSTANCE = this;
            DontDestroyOnLoad(this);
            if (partListLoadedEvent == null)
            {
                partListLoadedEvent = new EventVoid.OnEvent(onPartListLoaded);
                GameEvents.OnPartLoaderLoaded.Add(partListLoadedEvent);
            }

        }

        public void OnDestroy()
        {
            GameEvents.OnPartLoaderLoaded.Remove(partListLoadedEvent);
        }

        public void ModuleManagerPostLoad()
        {
            load();
        }

        internal void removeAPICheckGUI()
        {
            if (apiCheckGUI != null)
            {
                Component.Destroy(apiCheckGUI);
            }
        }

        private void load()
        {
            //clear any existing data in case of module-manager reload
            loadedShaders.Clear();
            loadedModelShaderSets.Clear();
            iconShaders.Clear();
            loadedTextureSets.Clear();
            textureColors.Clear();
            transparentShaderData.Clear();
            Log.log("TexturesUnlimited - Initializing shader and texture set data.");
            ConfigNode[] allTUNodes = GameDatabase.Instance.GetConfigNodes("TEXTURES_UNLIMITED");
            ConfigNode config = Array.Find(allTUNodes, m => m.GetStringValue("name") == "default");
            configurationNode = config;

            logAll = config.GetBoolValue("logAll", logAll);
            logReplacements = config.GetBoolValue("logReplacements", logReplacements);
            logErrors = config.GetBoolValue("logErrors", logErrors);
            recolorGUIWidth = config.GetIntValue("recolorGUIWidth");
            recolorGUITotalHeight = config.GetIntValue("recolorGUITotalHeight");
            recolorGUISectionHeight = config.GetIntValue("recolorGUISectionHeight");
            if (config.GetBoolValue("displayDX9Warning", true))
            {
                //disable API check as long as using stock reflection system
                //doAPICheck();
            }
            loadBundles();
            buildShaderSets();
            PresetColor.loadColors();
            Log.log("TexturesUnlimited: Loaded Colors Completed");
            loadTextureSets();
            Log.log("TexturesUnlimited: Loaded TextureSets Completed");
            applyToModelDatabase();
            Log.log("TexturesUnlimited - Calling PostLoad handlers");
            foreach (Action act in postLoadCallbacks) { act.Invoke(); }
            dumpUVMaps();
            fixStockBumpMaps();
            //NormMaskCreation.processBatch();
        }

        private void doAPICheck()
        {
            //check the graphics API, popup warning if using unsupported gfx (dx9/11/12/legacy-openGL)
            UnityEngine.Rendering.GraphicsDeviceType graphicsAPI = SystemInfo.graphicsDeviceType;
            if (graphicsAPI == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore)
            {
                //noop, everything is fine
            }
            else if (graphicsAPI == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
            {
                //works, but needs alternate render
                alternateRender = true;
            }
            else if (graphicsAPI == UnityEngine.Rendering.GraphicsDeviceType.Direct3D9)
            {
                //has issues -- display warning, and needs alternate render
                alternateRender = true;
                if (apiCheckGUI == null)
                {
                    apiCheckGUI = this.gameObject.AddComponent<GraphicsAPIGUI>();
                    apiCheckGUI.openGUI();
                }
            }
            else
            {
                //unknown API -- display warning
                if (apiCheckGUI == null)
                {
                    apiCheckGUI = this.gameObject.AddComponent<GraphicsAPIGUI>();
                    apiCheckGUI.openGUI();
                }
            }
        }

        private void onPartListLoaded()
        {
            Log.log("TexturesUnlimited - Updating Part Icon shaders.");
            applyToPartIcons();
        }

        private static void loadBundles()
        {
            loadedShaders.Clear();
            ConfigNode[] shaderNodes = GameDatabase.Instance.GetConfigNodes("KSP_SHADER_BUNDLE");
            int len = shaderNodes.Length;
            for (int i = 0; i < len; i++)
            {
                loadBundle(shaderNodes[i], loadedShaders);
            }
        }

        private static void loadBundle(ConfigNode node, Dictionary<String, Shader> shaderDict)
        {
            string assetBundleName = "";
            if (node.HasValue("universal")) { assetBundleName = node.GetStringValue("universal"); }
            else if (Application.platform == RuntimePlatform.WindowsPlayer) { assetBundleName = node.GetStringValue("windows"); }
            else if (Application.platform == RuntimePlatform.LinuxPlayer) { assetBundleName = node.GetStringValue("linux"); }
            else if (Application.platform == RuntimePlatform.OSXPlayer) { assetBundleName = node.GetStringValue("osx"); }
            assetBundleName = KSPUtil.ApplicationRootPath + "GameData/" + assetBundleName;

            Log.log("TexturesUnlimited - Loading Shader Pack: " + node.GetStringValue("name") + " :: " + assetBundleName);

            // KSP-PartTools built AssetBunldes are in the Web format, 
            // and must be loaded using a WWW reference; you cannot use the
            // AssetBundle.CreateFromFile/LoadFromFile methods unless you 
            // manually compiled your bundles for stand-alone use
            WWW www = CreateWWW(assetBundleName);

            if (!string.IsNullOrEmpty(www.error))
            {
                Log.exception("TexturesUnlimited - Error while loading shader AssetBundle: " + www.error);
                return;
            }
            else if (www.assetBundle == null)
            {
                Log.exception("TexturesUnlimited - Could not load AssetBundle from WWW - " + www);
                return;
            }

            AssetBundle bundle = www.assetBundle;

            string[] assetNames = bundle.GetAllAssetNames();
            int len = assetNames.Length;
            Shader shader;
            for (int i = 0; i < len; i++)
            {
                if (assetNames[i].EndsWith(".shader"))
                {
                    shader = bundle.LoadAsset<Shader>(assetNames[i]);
                    Log.log("TexturesUnlimited - Loaded Shader: " + shader.name + " :: " + assetNames[i]+" from pack: "+ node.GetStringValue("name"));
                    if (shader == null || string.IsNullOrEmpty(shader.name))
                    {
                        Log.exception("ERROR: Shader did not load properly for asset name: " + assetNames[i]);
                    }
                    else if (shaderDict.ContainsKey(shader.name))
                    {
                        Log.exception("ERROR: Duplicate shader detected: " + shader.name);
                    }
                    else
                    {
                        shaderDict.Add(shader.name, shader);
                    }
                    GameDatabase.Instance.databaseShaders.AddUnique(shader);
                }
            }
            //this unloads the compressed assets inside the bundle, but leaves any instantiated shaders in-place
            bundle.Unload(false);
        }

        public static void addPostLoadCallback(Action func)
        {
            postLoadCallbacks.AddUnique(func);
        }

        public static void removePostLoadCallback(Action func)
        {
            postLoadCallbacks.Remove(func);
        }

        private static void buildShaderSets()
        {
            ConfigNode[] shaderNodes = GameDatabase.Instance.GetConfigNodes("KSP_SHADER_DATA");
            ConfigNode node;
            int len = shaderNodes.Length;
            string sName, iName;
            for (int i = 0; i < len; i++)
            {
                node = shaderNodes[i];
                sName = node.GetStringValue("shader", "KSP/Diffuse");
                iName = node.GetStringValue("iconShader", "KSP/ScreenSpaceMask");
                Log.log("Loading shader icon replacement data for: " + sName + " :: " + iName);
                Shader shader = getShader(sName);
                if (shader == null)
                {
                    Log.exception("ERROR: Could not locate base Shader for name: " + sName + " while setting up icon shaders.");
                    continue;
                }
                Shader iconShader = getShader(iName);
                if (iconShader == null)
                {
                    Log.exception("ERROR: Could not locate icon Shader for name: " + iName + " while setting up icon shaders.");
                    continue;
                }
                IconShaderData data = new IconShaderData(shader, iconShader);
                iconShaders.Add(shader.name, data);
            }
        }

        private static void loadTransparencyData()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("TRANSPARENT_SHADER");
            TransparentShaderData tsd;
            int len = nodes.Length;
            for (int i = 0; i < len; i++)
            {
                tsd = new TransparentShaderData(nodes[i]);
                transparentShaderData.Add(tsd.shader.name, tsd);
            }
        }

        /// <summary>
        /// Asset bundle loader helper method.  Creates a Unity WWW URL reference for the input file-path
        /// </summary>
        /// <param name="bundlePath"></param>
        /// <returns></returns>
        private static WWW CreateWWW(string bundlePath)
        {
            try
            {
                string name = Application.platform == RuntimePlatform.WindowsPlayer ? "file:///" + bundlePath : "file://" + bundlePath;
                return new WWW(Uri.EscapeUriString(name));
            }
            catch (Exception e)
            {
                Log.exception("Error while creating AssetBundle request: " + e);
                return null;
            }
        }

        private static void loadTextureSets()
        {
            loadedTextureSets.Clear();
            ConfigNode[] setNodes = GameDatabase.Instance.GetConfigNodes("KSP_TEXTURE_SET");
            TextureSet[] sets = TextureSet.parse(setNodes, "create");
            int len = sets.Length;
            for (int i = 0; i < len; i++)
            {
                if (loadedTextureSets.ContainsKey(sets[i].name))
                {
                    Log.exception("ERROR: Duplicate texture set definition found for name: " + sets[i].name +
                        "  This is a major configuration error that should be corrected.  Correct operation cannot be ensured.");
                }
                else
                {
                    loadedTextureSets.Add(sets[i].name, sets[i]);
                }
            }
        }

        /// <summary>
        /// Applies any 'KSP_MODEL_SHADER' definitions to models in the GameDatabase.loadedModels list.
        /// </summary>
        private static void applyToModelDatabase()
        {
            ConfigNode[] modelShaderNodes = GameDatabase.Instance.GetConfigNodes("KSP_MODEL_SHADER");
            TextureSet set = null;
            ConfigNode textureNode;
            string setName="";
            int len = modelShaderNodes.Length;
            string[] modelNames;
            GameObject model;
            for (int i = 0; i < len; i++)
            {
                textureNode = modelShaderNodes[i];
                if (textureNode.HasNode("MATERIAL"))
                {
                    set = new TextureSet(textureNode, "update");
                    setName = set.name;
                }
                else if (textureNode.HasNode("TEXTURE"))//legacy style definitions
                {
                    set = new TextureSet(textureNode, "update");
                    setName = set.name;
                }
                else if (textureNode.HasValue("textureSet"))
                {
                    setName = textureNode.GetStringValue("textureSet");
                    set = getTextureSet(setName);
                    if (set == null)
                    {
                        Log.exception("ERROR: Did not locate texture set from global cache for input name: " + setName+" while applying KSP_MODEL_SHADER with name of: "+modelShaderNodes[i].GetStringValue("name","UNKNOWN"));
                        continue;
                    }
                }
                if (!string.IsNullOrEmpty(setName) && !loadedModelShaderSets.ContainsKey(setName))
                {
                    loadedModelShaderSets.Add(setName, set);
                }
                modelNames = textureNode.GetStringValues("model");
                int len2 = modelNames.Length;
                for (int k = 0; k < len2; k++)
                {
                    model = GameDatabase.Instance.GetModelPrefab(modelNames[k]);
                    if (model != null)
                    {
                        Log.replacement("TexturesUnlimited -- Replacing textures on database model: " + modelNames[k]);
                        set.enableHSV(model.transform, set.maskColorsHSV);
                        set.enableRGB(model.transform, set.maskColorsRGB);
                    }
                    else
                    {
                        Log.exception("ERROR: Could not locate model: " + modelNames[k] + " while applying KSP_MODEL_SHADER with name of: " + modelShaderNodes[i].GetStringValue("name", "UNKNOWN"));
                    }
                }
            }
        }

        /// <summary>
        /// Update the part-icons for any parts using shaders found in the part-icon-updating shader map.
        /// Adjusts models specifically based on what shader they are currently using, with the goal of replacing the stock default icon shader with something more suitable.
        /// </summary>
        private static void applyToPartIcons()
        {
            //brute-force method for fixing part icon shaders
            //  iterate through entire loaded parts list
            //      iterate through every transform with a renderer component
            //          if renderer uses a shader in the shader-data-list
            //              replace shader on icon with the 'icon shader' corresponding to the current shader
            Shader iconShader;
            foreach (AvailablePart p in PartLoader.LoadedPartsList)
            {
                if (p.iconPrefab == null)//should never happen
                {
                    Log.exception("ERROR: Part: " + p.name + " had a null icon!");
                    continue;
                }
                if (p.partPrefab == null)
                {
                    Log.exception("ERROR: Part: " + p.name + " had a null prefab!");
                    continue;
                }
                bool outputName = false;//only log the adjustment a single time
                Transform pt = p.partPrefab.gameObject.transform;
                Renderer[] ptrs = pt.GetComponentsInChildren<Renderer>();
                foreach (Renderer partRenderer in ptrs)
                {
                    Material originalMeshMaterial = partRenderer.sharedMaterial;
                    if (originalMeshMaterial == null || partRenderer.sharedMaterial.shader == null)
                    {
                        if (originalMeshMaterial == null) { Log.exception("ERROR: Null material found on renderer: " + partRenderer.gameObject.name); }
                        else if (originalMeshMaterial.shader == null) { Log.exception("ERROR: Null shader found on renderer: " + partRenderer.gameObject.name); }
                        continue;
                    }
                    //part transform shader name
                    string materialShaderName = originalMeshMaterial.shader.name;
                    if (!string.IsNullOrEmpty(materialShaderName) && iconShaders.ContainsKey(materialShaderName))//is a shader that we care about
                    {
                        iconShader = iconShaders[materialShaderName].iconShader;
                        if (!outputName)
                        {
                            Log.replacement("KSPShaderLoader - Adjusting icon shaders for part: " + p.name + " for original shader:" + materialShaderName + " replacement: " + iconShader.name);
                            outputName = true;
                        }
                        //transforms in the icon prefab
                        //adjust the materials on these to use the specified shader from config
                        Transform[] iconPrefabTransforms = p.iconPrefab.gameObject.transform.FindChildren(partRenderer.name);//find transforms from icon with same name
                        foreach (Transform ictr in iconPrefabTransforms)
                        {
                            Renderer itr = ictr.GetComponent<Renderer>();
                            if (itr == null) { continue; }
                            Material mat2 = itr.material;//use .material to force non-shared material instances
                            if (mat2 == null) { continue; }
                            Log.replacement("BASE:\n" + Debug.getMaterialPropertiesDebug(originalMeshMaterial));
                            Log.replacement("PRE :\n" + Debug.getMaterialPropertiesDebug(mat2));
                            //can't just swap shaders, does some weird stuff with properties??
                            mat2.shader = iconShader;
                            //mat2.CopyPropertiesFromMaterial(originalMeshMaterial);
                            //mat2.CopyKeywordsFrom(originalMeshMaterial);
                            itr.material = mat2;//probably un-needed, but whatever
                            if (originalMeshMaterial.HasProperty("_Shininess") && mat2.HasProperty("_Smoothness"))
                            {
                                mat2.SetFloat("_Smoothness", originalMeshMaterial.GetFloat("_Shininess"));
                            }
                            Log.replacement("POST:\n" + Debug.getMaterialPropertiesDebug(mat2));
                            //TODO -- since these parts have already been mangled and had the stock icon shader applied
                            //  do any properties not present on stock parts need to be re-seated, or do they stay resident in
                            //  the material even if the current shader lacks the property?
                            //TODO -- check the above, esp. in regards to keywords now that TU is using them
                            //  need to make sure keywords stay resident in the material itself between shader swaps.
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Utility method to dump UV maps from every model currently in the model database.
        /// TODO -- has issues/errors on some models/meshes/renderers (might be a skinned-mesh-renderer problem...)
        /// TODO -- has issues with part names that have invalid characters for file-system use -- should sanitize the names
        /// </summary>
        public static void dumpUVMaps(bool force = false)
        {
            UVMapExporter exporter = new UVMapExporter();
            ConfigNode node = TexturesUnlimitedLoader.configurationNode.GetNode("UV_EXPORT");
            bool export = node.GetBoolValue("exportUVs", false);
            if (!export && !force) { return; }
            string path = node.GetStringValue("exportPath", "exportedUVs");
            exporter.width = node.GetIntValue("width", 1024);
            exporter.height = node.GetIntValue("height", 1024);
            exporter.stroke = node.GetIntValue("thickness", 1);
            foreach (GameObject go in GameDatabase.Instance.databaseModel)
            {
                exporter.exportModel(go, path);
            }
        }

        /// <summary>
        /// Runs through a list of configs and fixes any stock Normal Map textures that are incorrectly formatted for use with the Unity Standard shader.
        /// Each texture to be corrected must be specified separately in a config file.
        /// </summary>
        public static void fixStockBumpMaps()
        {
            long elapsedTime = 0;
            ConfigNode[] rootNodes = GameDatabase.Instance.GetConfigNodes("STOCK_NORMAL_CORRECTION");
            if (rootNodes == null || rootNodes.Length <= 0)
            {
                Log.debug("Stock normal correction nodes were null! - Nothing to correct.");
                return;
            }
            Stopwatch sw = new Stopwatch();
            foreach (ConfigNode rootNode in rootNodes)
            {
                ConfigNode[] texNodes = rootNode.GetNodes("TEXTURE");
                foreach (ConfigNode texNode in texNodes)
                {
                    sw.Start();
                    string texName = texNode.GetStringValue("name");
                    string xSource = texNode.GetStringValue("xSourceChannel");
                    string ySource = texNode.GetStringValue("ySourceChannel");

                    Log.debug("TexturesUnlimited - Correcting Stock Normal Map: " + texName + " xSource: " + xSource + " ySource: " + ySource);

                    GameDatabase.TextureInfo info = GameDatabase.Instance.GetTextureInfo(texName);
                    if (info == null)
                    {
                        Log.debug("ERROR: Source texture was null for path: " + texName);
                        continue;
                    }
                    Texture2D sourceTexture = info.texture;
                    if (sourceTexture == null)
                    {
                        Log.debug("ERROR: Source texture was null for path: " + texName);
                        continue;
                    }

                    if (!sourceTexture.isReadable)
                    {
                        Log.debug("Source Texture is unreadable, blitting through RenderTexture to make readable.");
                        Log.debug("Source Texture format: " + sourceTexture.format);
                        RenderTexture blitTarget = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                        Graphics.Blit(sourceTexture, blitTarget);
                        RenderTexture prev = RenderTexture.active;
                        RenderTexture.active = blitTarget;
                        Texture2D newSource = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.ARGB32, false);
                        newSource.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0);
                        newSource.Apply(true, false);
                        sourceTexture = newSource;
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(blitTarget);
                    }

                    Texture2D temp = new Texture2D(sourceTexture.width, sourceTexture.height, sourceTexture.format, sourceTexture.mipmapCount > 0);
                    Color[] sourcePixels = sourceTexture.GetPixels(0, 0, sourceTexture.width, sourceTexture.height);
                    int len = sourcePixels.Length;
                    Color c, c1;
                    float r, g, b, a;
                    for (int i = 0; i < len; i++)
                    {
                        c = sourcePixels[i];//source pixel
                        c1 = c;//copy of source pixel
                        r = c.r;
                        g = c.g;
                        b = c.b;
                        a = c.a;

                        c.r = 1;
                        c.g = getChannelColor(c1, ySource);
                        c.b = 1;
                        c.a = getChannelColor(c1, xSource);
                        sourcePixels[i] = c;
                    }
                    temp.SetPixels(0, 0, sourceTexture.width, sourceTexture.height, sourcePixels);
                    temp.Apply(true, true);
                    info.texture = temp;
                    sw.Stop();
                    elapsedTime += sw.ElapsedMilliseconds;
                    Log.debug("Texture update time: " + sw.ElapsedMilliseconds + " ms.");
                    sw.Reset();
                }
            }
            Log.debug("Total texture correction time: " + elapsedTime + " ms.");
        }

        private static float getChannelColor(Color color, string channel)
        {
            if (channel == "r") { return color.r; }
            if (channel == "g") { return color.g; }
            if (channel == "b") { return color.b; }
            if (channel == "a") { return color.a; }
            Log.debug("Unrecognized channel specified: " + channel + ".  This must be either 'r', 'g', 'b', or 'a'.  Using value from 'a' channel as default.");
            return color.a;
        }

        /// <summary>
        /// Return a shader by name.  First checks the TU shader dictionary, then checks the GameDatabase.databaseShaders list, and finally falls-back to standard Unity Shader.Find() method.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Shader getShader(string name)
        {
            if (loadedShaders.ContainsKey(name))
            {
                return loadedShaders[name];
            }
            Shader s = GameDatabase.Instance.databaseShaders.Find(m => m.name == name);
            if (s != null)
            {
                return s;
            }
            return Shader.Find(name);
        }

        /// <summary>
        /// Find a global texture set from model shader set cache with a name that matches the input name.  Returns null if not found.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static TextureSet getModelShaderTextureSet(string name)
        {
            TextureSet s = null;
            if (loadedModelShaderSets.TryGetValue(name, out s))
            {
                return s;
            }
            Log.exception("ERROR: Could not locate TextureSet for MODEL_SHADER from global cache for the input name of: " + name);
            return null;
        }

        /// <summary>
        /// Find a global texture set from database with a name that matches the input name.  Returns null if not found.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static TextureSet getTextureSet(string name)
        {
            TextureSet s = null;
            if (loadedTextureSets.TryGetValue(name, out s))
            {
                return s;
            }
            Log.exception("ERROR: Could not locate TextureSet from global cache for the input name of: " + name);
            return null;
        }

        /// <summary>
        /// Return an array of texture sets for the 'name' values from within the input config node array.  Returns an empty array if none are found.
        /// </summary>
        /// <param name="setNodes"></param>
        /// <returns></returns>
        public static TextureSet[] getTextureSets(ConfigNode[] setNodes)
        {
            int len = setNodes.Length;
            TextureSet[] sets = new TextureSet[len];
            for (int i = 0; i < len; i++)
            {
                sets[i] = getTextureSet(setNodes[i].GetStringValue("name"));
            }
            return sets;
        }

        /// <summary>
        /// Return an array of texture sets for the values from within the input string array.  Returns an empty array if none are found.
        /// </summary>
        /// <param name="setNodes"></param>
        /// <returns></returns>
        public static TextureSet[] getTextureSets(string[] setNames)
        {
            int len = setNames.Length;
            TextureSet[] sets = new TextureSet[len];
            for (int i = 0; i < len; i++)
            {
                sets[i] = getTextureSet(setNames[i]);
            }
            return sets;
        }

        /// <summary>
        /// Input should be a string with R,G,B,A values specified in comma-separated byte notation
        /// </summary>
        /// <param name="stringColor"></param>
        /// <returns></returns>
        public static Texture2D getTextureColor(string stringColor)
        {
            string rgbaString;
            Color c = Utils.parseColor(stringColor);
            //just smash the entire thing together to create a unique key for the color
            rgbaString = "" + c.r +":"+ c.g + ":" + c.b + ":" + c.a;
            Texture2D tex = null;
            if (textureColors.TryGetValue(rgbaString, out tex))
            {
                return tex;
            }
            else
            {
                int len = 64 * 64;
                Color[] pixelData = new Color[len];
                for (int i = 0; i < len; i++)
                {
                    pixelData[i] = c;
                }
                tex = new Texture2D(64, 64, TextureFormat.ARGB32, false);
                tex.name = "TUTextureColor:" + rgbaString;
                tex.SetPixels(pixelData);
                tex.Apply(false, true);
                textureColors.Add(rgbaString, tex);
                return tex;
            }
        }

        /// <summary>
        /// Return true/false if the input material uses a shader that supports transparency
        /// AND transparency is currently enabled on the material from keywords (if applicable).
        /// </summary>
        /// <param name="mat"></param>
        /// <returns></returns>
        public static bool isTransparentMaterial(Material mat)
        {
            return isTransparentShader(mat.shader.name);
        }

        public static bool isTransparentShader(string name)
        {
            TransparentShaderData tsd = null;
            if (transparentShaderData.TryGetValue(name, out tsd))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    }

    public class TransparentShaderData
    {
        public readonly Shader shader;
        public readonly string shaderName;

        public TransparentShaderData(ConfigNode node)
        {
            shaderName = node.GetStringValue("name");
            shader = TexturesUnlimitedLoader.getShader(shaderName);
        }
    }

    /// <summary>
    /// Shader to IconShader map <para/>
    /// Used to fix incorrect icon shaders when recoloring shaders are used.
    /// </summary>
    public class IconShaderData
    {
        public readonly Shader shader;
        public readonly Shader iconShader;

        public IconShaderData(Shader shader, Shader iconShader)
        {
            this.shader = shader;
            this.iconShader = iconShader;
        }
    }
    
    // TODO: Detail in preset
    public struct RecoloringDataPreset
    {
        public string name;
        public string title;
        public uColor colorHSV;
        public Color colorRGB;
        public float specular;
        public float metallic;
        public float detail;
        public bool isFavorite;
        public bool isTemp;
        public bool isHidden;

        public RecoloringDataPreset(ConfigNode node)
        {
            name = node.GetStringValue("name");
            title = node.GetStringValue("title");
            colorRGB = node.GetColor("color");
            colorHSV = uColor.fromShaderColor(colorRGB);
            specular = node.GetColorChannelValue("specular");
            metallic = node.GetColorChannelValue("metallic");
            detail = node.GetColorChannelValue("detail");
            isFavorite = node.GetBoolValue("favorite");
            isTemp = node.GetBoolValue("temporary");
            isHidden = node.GetBoolValue("hidden");
        }

        public HSVRecoloringData getHSVRecoloringData()
        {
            return new HSVRecoloringData(colorHSV, specular, metallic, detail);
        }

        public RecoloringData getRecoloringData()
        {
            return new RecoloringData(colorRGB, specular, metallic, detail);
        }

        /// <summary>
        /// Checks for color property similarity but not flag similarity
        /// </summary>
        public static bool operator ==(RecoloringDataPreset one, RecoloringDataPreset two)
        {
            return (one.name == two.name &&
                    one.title == two.title &&
                    (one.colorRGB == two.colorRGB || one.colorHSV == two.colorHSV) &&
                    Mathf.Approximately(one.specular, two.specular) &&
                    Mathf.Approximately(one.metallic, two.metallic) &&
                    Mathf.Approximately(one.detail, two.detail));
        }
        /// <summary>
        /// Checks for color property similarity but not flag similarity
        /// </summary>
        public static bool operator !=(RecoloringDataPreset one, RecoloringDataPreset two)
        {
            return !(one == two);
        }
    }

    /// <summary>
    /// Defines a group of presets colors to be made available in the recoloring GUI.  These will be defined by root-level configuration nodes, in the form of:
    /// PRESET_COLOR_GROUP
    /// {
    ///     name = unique_name_here
    ///     color = name_of_preset_color
    ///     color = name_of_another_preset_color
    /// }
    /// </summary>
    public class RecoloringDataPresetGroup
    {
        public string name;
        public List<RecoloringDataPreset> colors = new List<RecoloringDataPreset>();
        public RecoloringDataPresetGroup(string name) { this.name = name; }
    }

    public class PresetColor
    {
        // Dictionary to map string-based names to HEX10-based names; for outside parts loading default/external preset colors
        public static Dictionary<string, string> obsoleteColorMap = new Dictionary<string, string>();
        private static List<RecoloringDataPreset> colorList = new List<RecoloringDataPreset>();
        private static Dictionary<string, RecoloringDataPreset> presetColors = new Dictionary<string, RecoloringDataPreset>();
        private static List<RecoloringDataPresetGroup> presetGroupList = new List<RecoloringDataPresetGroup>();
        private static Dictionary<string, RecoloringDataPresetGroup> presetGroups = new Dictionary<string, RecoloringDataPresetGroup>();
        
        
        // TODO: Add real support for hidden colors - aka recents that don't show up in preset groups
        internal static void loadColors()
        {
            colorList.Clear();
            presetColors.Clear();
            
            sanitizePresetsFromPrimaryFile();

            ConfigNode[] masterColorNodes = GameDatabase.Instance.GetConfigNodes("COLOR_MASTER");
            ConfigNode[] masterGroupNodes = GameDatabase.Instance.GetConfigNodes("GROUP_MASTER");

            ConfigNode primaryMasterColorNode =
                GameDatabase.Instance.GetConfigNode("000_TexturesUnlimited/ColorPresets/COLOR_MASTER");
            ConfigNode primaryMasterGroupNode = 
                GameDatabase.Instance.GetConfigNode("000_TexturesUnlimited/ColorPresets/COLOR_MASTER");

            // Check for external sources
            Dictionary<string, RecoloringDataPreset> externalColors = new Dictionary<string, RecoloringDataPreset>();
            if (GameDatabase.Instance.GetConfigNodes("KSP_COLOR_PRESET").Length != 0)
            {
                int count;
                Log.log($"TexturesUnlimited: External color preset sources detected of #: " +
                        $"{count = GameDatabase.Instance.GetConfigNodes("KSP_COLOR_PRESET").Length} with " +
                        $"{(primaryMasterColorNode != null ? (primaryMasterColorNode.GetNodes("KSP_COLOR_PRESET").Length - count).ToString() : "0")} internal");
                
                ConfigNode[] nodes =  GameDatabase.Instance.GetConfigNodes("KSP_COLOR_PRESET");
                foreach (var configNode in nodes)
                {
                    var sanitized = sanitizeColor(configNode);
                    RecoloringDataPreset preset = new RecoloringDataPreset(sanitized.Item1);
                    preset.isTemp = true;
                    externalColors.Add(configNode.GetValue("name"),  preset);
                    Log.debug($"Adding new obsolete pairing {sanitized.Item2} {preset} to legacy");
                    obsoleteColorMap.TryAdd(configNode.GetValue("name"), sanitized.Item2);
                    if (!presetColors.TryAdd(sanitized.Item2, preset))
                    {
                        presetColors.Add(sanitized.Item2, preset);
                    };
                    colorList.Add(preset);
                    loadPresetIntoGroup(preset, "FULL");
                }
            }
            
            
            if (GameDatabase.Instance.GetConfigNodes("PRESET_COLOR_GROUP").Length != 0)
            {
                int count;
                Log.log($"TexturesUnlimited: External preset group sources detected of #: {count = GameDatabase.Instance.GetConfigNodes("PRESET_COLOR_GROUP").Length} with "+
                $"{(primaryMasterGroupNode != null ? (primaryMasterGroupNode.GetNodes("KSP_COLOR_PRESET").Length - count).ToString() : "0")} internal");
                
                ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("PRESET_COLOR_GROUP");
                foreach (var configNode in nodes)
                {
                    string name = configNode.GetStringValue("name");
                    string[] colorNames = configNode.GetStringValues("color");
                    foreach (var colorName in colorNames)
                    {
                        // Attempt to match old name to a RecoloringDataPreset with a HEX10 name, then find color in cache
                        if (externalColors.TryGetValue(colorName, out var preset) && presetColors.TryGetValue(preset.name, out var data))
                        {
                            loadPresetIntoGroup(data, name);
                        }
                    }
                }
            }
            
            
            
            
            if (masterColorNodes.Length > 0)
            {
                foreach (var masterColorNode in masterColorNodes)
                {
                    ConfigNode[] colorNodes = masterColorNode.GetNodes("KSP_COLOR_PRESET");
                    foreach (var configNode in colorNodes)
                    {
                        RecoloringDataPreset data = new RecoloringDataPreset(configNode);
                        if (!presetColors.TryAdd(data.name, data)) continue;
                        colorList.Add(data);
                        loadPresetIntoGroup(data, data.isHidden ? "Hidden" : "FULL");
                        if (data.isFavorite) {loadPresetIntoGroup(data, "Favorite");}
                    }
                }
            }

            if (masterGroupNodes.Length > 0)
            {
                foreach (var masterGroupNode in masterGroupNodes)
                {
                    ConfigNode[] groupNodes = masterGroupNode.GetNodes("PRESET_COLOR_GROUP");
                    foreach (var configNode in groupNodes)
                    {
                        string name = configNode.GetStringValue("name");
                        string[] colorNames = configNode.GetStringValues("color");
                        foreach (var colorName in colorNames)
                        {
                            if (presetColors.TryGetValue(colorName, out var data))
                            {
                                loadPresetIntoGroup(data, name);
                            }
                        }
                    }
                }
            }
        }


        public static void deleteColorFromCache(RecoloringDataPreset data)
        {
            bool existsAsTitle;
            // Preset does not exist
            if (!(existsAsTitle = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select
                        (presetColors, preset => preset.Value.title)).Contains(data.title)) 
                && !presetColors.ContainsKey(data.title))
            {
                return;
            }

            // Preset exists as a HEX10 but not in name, replace that HEX10
            // TODO: overwrite protection
            if (presetColors.ContainsKey(data.name) && !existsAsTitle)
            {
                List<RecoloringDataPreset> presets = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(colorList, recoloringDataPreset => recoloringDataPreset.name != data.name));
                presetColors.Remove(data.name);
                
                List<RecoloringDataPresetGroup> groups = new List<RecoloringDataPresetGroup>();
                Dictionary<string, RecoloringDataPresetGroup> groupDict =  new Dictionary<string, RecoloringDataPresetGroup>();
                foreach (var (name,  group) in presetGroups)
                {
                    if (group.colors.Contains(data))
                    {
                        group.colors.Remove(data);
                        continue;
                    }
                    groups.Add(group);
                    groupDict.Add(name, group);
                }

                presetGroups = groupDict;
                presetGroupList = groups;
                colorList = presets;
                return;
            }

            // Exists as a title but a different HEX10
            if (!presetColors.ContainsKey(data.name) && existsAsTitle)
            {
                string cachedName = "";
                List<RecoloringDataPreset> presets = new List<RecoloringDataPreset>();
                Dictionary<string, RecoloringDataPreset> presetDict = new Dictionary<string, RecoloringDataPreset>();
                foreach (var (name, preset) in presetColors)
                {
                    if (preset.title != data.title)
                    {
                        presets.Add(preset);
                        presetDict.Add(name, preset);
                        continue;
                    }
                    cachedName = name;
                }
                
                List<RecoloringDataPresetGroup> groups = new List<RecoloringDataPresetGroup>();
                Dictionary<string, RecoloringDataPresetGroup> groupDict = new Dictionary<string, RecoloringDataPresetGroup>();
                foreach (var (name, group) in presetGroups)
                {
                    List<RecoloringDataPreset> colors =
                        System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(group.colors,
                            preset => preset.name != cachedName));
                    group.colors = colors;
                    groups.Add(group);
                    groupDict.Add(name, group);
                }

                colorList = presets;
                presetColors = presetDict;
                presetGroupList = groups;
                presetGroups = groupDict;
                return;
            }

            // Exists as an identical HEX10 and title (default delete functionality)
            if (presetColors.ContainsKey(data.name) && existsAsTitle)
            {
                List<RecoloringDataPreset> presets = new List<RecoloringDataPreset>();
                Dictionary<string, RecoloringDataPreset> presetDict = new Dictionary<string, RecoloringDataPreset>();
                foreach (var (name, preset) in presetColors)
                {
                    if (preset.title == data.title) continue;
                    presets.Add(preset);
                    presetDict.Add(name, preset);
                }

                List<RecoloringDataPresetGroup> groups = new List<RecoloringDataPresetGroup>();
                Dictionary<string, RecoloringDataPresetGroup> groupDict = new Dictionary<string, RecoloringDataPresetGroup>();
                foreach (var (name, group) in presetGroups)
                {
                    List<RecoloringDataPreset> colors =
                        System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(group.colors,
                            preset => preset.name != data.name));
                    group.colors = colors;
                    groups.Add(group);
                    groupDict.Add(name, group);
                }

                colorList = presets;
                presetColors = presetDict;
                presetGroupList = groups;
                presetGroups = groupDict;
                return;
            }
            
            Log.log("TexturesUnlimited: Attempting to delete a preset that does not exist in any capacity.");
        }
        // TODO: reload presets from defaults (remove all customs and re-create all deleted)
// TODO: overwrite ability
        /// <summary>
        /// Creates a new Preset Color in proper cache lists from an existing data preset
        /// </summary>
        public static void createColorToCache(RecoloringDataPreset data)
        {
            if (!presetColors.TryAdd(data.name, data)) return;
            colorList.Add(data);
            if (data.isTemp) return;
            loadPresetIntoGroup(data, "FULL");
            loadPresetIntoGroup(data, "Custom");
            if (data.isFavorite) loadPresetIntoGroup(data, "Favorite");
        }

        /// <summary>
        /// Creates a new Preset Color in proper cache lists from an HSVRecoloringData and necessary accessory information
        /// </summary>
        public static void createColorToCache(HSVRecoloringData data, string name, string title, bool isFavorite, bool isTemporary, bool isHidden)
        {
            var preset = new RecoloringDataPreset()
            {
                name = name,
                title = title,
                colorHSV = data.color,
                colorRGB = uColor.toShaderColor(data.color),
                specular = data.specular,
                metallic = data.metallic,
                isFavorite = isFavorite,
                isHidden = isHidden,
                isTemp = isTemporary
            };
            createColorToCache(preset);
        }

        /// <summary>
        /// Takes in a HSVRecoloringData object and an optional display name. Attempts to edit the corresponding cache preset, creates a new one if it doesn't exist
        /// </summary>
        /// <returns>True if edited, False if new preset created</returns>
        public static void editColorFromCache(HSVRecoloringData data, string title = "Unknown")
        {
            if (presetColors.TryGetValue(HSVRecoloringData.ConvertToHEXTwelve(data), out var dataPreset))
            {
                editColorFromCache(dataPreset);
            }
            createColorToCache(data, HSVRecoloringData.ConvertToHEXTwelve(data), title, false, false, false);
        }
        
        public static void editColorFromCache(RecoloringDataPreset data)
        {
            List<RecoloringDataPreset> oldPresets = new List<RecoloringDataPreset>();
            foreach (var preset in colorList)
            {
                if (preset.title == data.title)
                {
                    oldPresets.Add(data);
                    presetColors.Remove(preset.name);
                    presetColors.TryAdd(data.name, data);
                }
                else
                {
                    oldPresets.Add(preset);
                }
            }
            colorList = oldPresets;
            
            List<RecoloringDataPreset> oldGroups = new List<RecoloringDataPreset>();
            foreach (var presetGroup in presetGroupList)
            {
                List<RecoloringDataPreset> colors = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(
                    presetGroup.colors, preset => preset.title == data.title ? data : preset));
                presetGroup.colors = colors;
                presetGroups[presetGroup.name] = presetGroup;
            }
        }

        public static bool writeColorsToFile()
        {
            ConfigNode master = new ConfigNode("KSP_MASTER");
            ConfigNode primaryColorMaster = GameDatabase.Instance.GetConfigNode("000_TexturesUnlimited/ColorPresets/COLOR_MASTER");
            ConfigNode primaryGroupMaster = GameDatabase.Instance.GetConfigNode("000_TexturesUnlimited/ColorPresets/GROUP_MASTER");

            // TODO: duplicate protection for these dictionaries
            Dictionary<string, ConfigNode> colorFromFile = System.Linq.Enumerable.ToDictionary(primaryColorMaster.GetNodes("KSP_COLOR_PRESET"), configNode => configNode.GetValue("name") == string.Empty ? "Custom:"+configNode.GetValue("title") : configNode.GetValue("name"));
            Dictionary<string, ConfigNode> groupFromFile = System.Linq.Enumerable.ToDictionary(primaryGroupMaster.GetNodes("PRESET_COLOR_GROUP"), configNode => configNode.GetValue("name") == string.Empty ? "full" : configNode.GetValue("name"));
            
            List<ConfigNode> colorToFile = new List<ConfigNode>();
            List<ConfigNode> groupToFile = new List<ConfigNode>();

            foreach (var (name, preset) in presetColors)
            {
                if (preset.isTemp) continue;
                if (colorFromFile.TryGetValue(name, out ConfigNode fileNode))
                {
                    if (!fileNode.HasValue("detail")) fileNode.AddValue("detail", preset.detail);
                    RecoloringDataPreset filePreset = new RecoloringDataPreset(fileNode);
                    if (filePreset == preset && (filePreset.isTemp == preset.isTemp &&
                          filePreset.isHidden == preset.isHidden &&
                          filePreset.isFavorite == preset.isFavorite))
                    {
                        // identical presets
                        // if (colorToFile.Contains)
                        colorToFile.Add(fileNode);
                        continue;
                    }
                    if (filePreset == preset)
                    {
                        // identical color, but non-identical flags. overwrite
                        fileNode.SetValue("favorite", preset.isFavorite, true);
                        fileNode.SetValue("temporary", preset.isTemp, true);
                        fileNode.SetValue("hidden", preset.isHidden, true);
                        
                        colorToFile.Add(fileNode);
                        continue;
                    }
                }
                // new color
                fileNode = new ConfigNode("KSP_COLOR_PRESET");
                fileNode.AddValue("name", preset.name);
                fileNode.AddValue("title", preset.title);
                fileNode.AddValue("color", $"{(preset.colorRGB.r * 255):F0}," +
                                           $"{(preset.colorRGB.g * 255):F0}," + 
                                           $"{(preset.colorRGB.b * 255):F0}");
                fileNode.AddValue("specular", (preset.specular * 255).ToString("F0"));
                fileNode.AddValue("metallic", (preset.metallic * 255).ToString("F0"));
                fileNode.AddValue("detail", (preset.detail * 100).ToString("F0"));
                fileNode.AddValue("isFavorite", preset.isFavorite);
                fileNode.AddValue("isTemp", preset.isTemp);
                fileNode.AddValue("isHidden", preset.isHidden);
                
                colorToFile.Add(fileNode);
            }
            
            foreach (var (name, group) in presetGroups)
            {
                // The preset group FULL should not actually be written to file, it is implied
                if (name == "FULL") continue;
                // its possible none of this is needed and should just be created straight from presetGroups
                if (groupFromFile.TryGetValue(name, out ConfigNode fileNode))
                {
                    HashSet<string> fileList = new HashSet<string>(fileNode.GetValuesList("color"));
                    List<string> cacheList = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(group.colors, recoloringDataPreset => recoloringDataPreset.name));
                    if (name == fileNode.GetValue("name") && fileList.SetEquals(cacheList))
                    {
                        // identical preset group
                        groupToFile.Add(fileNode);
                        continue;
                    }
                    if (name == fileNode.GetValue("name"))
                    {
                        // same preset group
                        fileNode = new ConfigNode("PRESET_COLOR_GROUP");
                        fileNode.RemoveValue("color");
                        foreach (var preset in group.colors)
                        {
                            if (preset.isTemp) continue;
                            fileNode.AddValue("color", preset.name);
                        }

                        groupToFile.Add(fileNode);
                        continue;
                    }
                }
                // new preset
                fileNode = new ConfigNode("PRESET_COLOR_GROUP");
                fileNode.SetValue("name", name, true);
                foreach (var preset in group.colors)
                {
                    fileNode.AddValue("color", preset.name);
                }

                groupToFile.Add(fileNode);
            }
            
            primaryColorMaster.ClearNodes();
            primaryGroupMaster.ClearNodes();
            foreach (var node in colorToFile)
            {
                primaryColorMaster.AddNode(node);
            }
            foreach (var node in groupToFile)
            {
                primaryGroupMaster.AddNode(node);
            }

            // Create a pairwise map from old text-based naming to new HEX10-based naming
            // Provides support for config files (like parts) that reference old preset names
            // Should be discouraged in new parts, but possible if working with HEX10s is not desired
            ConfigNode obsoleteNameMap = new ConfigNode("LEGACY_MASTER");
            foreach (var (legacy, modern) in obsoleteColorMap)
            {
                ConfigNode node = new ConfigNode("LEGACY_PAIRING");
                node.AddValue("legacy", legacy);
                node.AddValue("modern", modern);
                obsoleteNameMap.AddNode(node);
            }
            
            master.AddNode(primaryColorMaster);
            master.AddNode(primaryGroupMaster);
            master.AddNode(obsoleteNameMap);
            GameDatabase.CompileConfig(master);
            return master.Save(KSPUtil.ApplicationRootPath + "/GameData/000_TexturesUnlimited/ColorPresets.cfg", "KSP_MASTER");
        }

        /// <summary>
        /// Takes in a ConfigNode of dubious compliance and returns a 2-tuple without modifying configIn
        /// </summary>
        /// <param name="configIn">(ConfigNode)</param>
        /// <returns>[configOut, cachedName] (ConfigNode, string)</returns>
        public static (ConfigNode, string) sanitizeColor(ConfigNode configIn)
        {
            var configOut = configIn.CreateCopy();
            string cachedName = HSVRecoloringData.ConvertToHEXTwelve(new RecoloringDataPreset(configIn));
            string color = "255, 255, 255";
            configIn.TryGetValue("color", ref color);
            // string strColor = $"{(color.r * 255):F0}," +
            //     $"{(color.g * 255):F0}," + 
            //     $"{(color.b * 255):F0}";
            string cachedTitle = configIn.GetStringValue("title", cachedName);
            int specular = configIn.GetIntValue("specular", 0);
            int metallic = configIn.GetIntValue("metallic", 0);
            int detail = configIn.GetIntValue("detail", 0);
            bool favorite = configIn.GetBoolValue("favorite", false);
            bool temporary = configIn.GetBoolValue("temporary", false);
            bool hidden = configIn.GetBoolValue("hidden", false);

            configOut.SetValue("name", cachedName, true);
            configOut.SetValue("color", color, true);
            configOut.SetValue("title", cachedTitle, true);
            configOut.SetValue("specular", specular, true);
            configOut.SetValue("metallic", metallic, true);
            configOut.SetValue("detail", detail, true);
            configOut.SetValue("favorite", favorite, true);
            configOut.SetValue("temporary", temporary, true);
            configOut.SetValue("hidden", hidden, true);

            return (configOut, cachedName);
        }

        // TODO: the dict TryAdd calls will prevent exceptions on load but results in identical colors being silently deleted with no user output
        internal static void sanitizePresetsFromPrimaryFile()
        {
            ConfigNode master = new ConfigNode("KSP_MASTER");
            Log.log("TexturesUnlimited: Sanitizing colors in main config file");
            
            ConfigNode primaryColorMaster = GameDatabase.Instance.GetConfigNode("000_TexturesUnlimited/ColorPresets/COLOR_MASTER");
            ConfigNode primaryGroupMaster = GameDatabase.Instance.GetConfigNode("000_TexturesUnlimited/ColorPresets/GROUP_MASTER");
            ConfigNode primaryLegacyMaster = GameDatabase.Instance.GetConfigNode("000_TexturesUnlimited/ColorPresets/LEGACY_MASTER");
            
            Dictionary<string, ConfigNode> colorDict = new Dictionary<string, ConfigNode>(); // ConfigNodes needing updating: writtenName, ConfigNode
            Dictionary<string, ConfigNode> colorDictCached = new Dictionary<string, ConfigNode>(); // ConfigNodes in format: cachedName, ConfigNode
            Dictionary<string, ConfigNode> groupDict = new Dictionary<string, ConfigNode>();
            Dictionary<string, ConfigNode> groupDictCached = new Dictionary<string, ConfigNode>();
            
            ConfigNode colorMaster;
            ConfigNode groupMaster;
            ConfigNode legacyMaster;
            
            if (primaryColorMaster != null)
            {
                colorMaster = primaryColorMaster;
                foreach (var configNode in colorMaster.GetNodes("KSP_COLOR_PRESET"))
                {
                    if (!configNode.HasData || !configNode.HasValue("color")) continue;
                    if (!configNode.HasValue("name"))
                    {
                        string str = "";
                        int spec = 0;
                        int met = 0;
                        int det = 0;
                        HSVRecoloringData data = new HSVRecoloringData()
                        {
                            color = configNode.TryGetValue("name", ref str) ? Utils.HSVParseColor(str) : uColor.white,
                            specular = configNode.TryGetValue("specular", ref spec) ? spec : 0,
                            metallic = configNode.TryGetValue("metallic", ref met) ? met : 0,
                            detail = configNode.TryGetValue("detail", ref det) ? det : 0
                        };
                        configNode.AddValue("name", HSVRecoloringData.ConvertToHEXTwelve(data));
                    }
                    if (configNode.GetValue("name").Contains("#") && configNode.GetValue("name").Length == 13)
                    {
                        if (!(configNode.HasValues("specular", "metallic", "detail")))
                        {
                            var newConfig = sanitizeColor(configNode);
                            colorDictCached.TryAdd(newConfig.Item2, newConfig.Item1);
                            continue;
                        }
                        colorDictCached.TryAdd(configNode.GetValue("name"), configNode);
                        continue;
                    }
                    if (configNode.GetColor("color").Equals(Color.clear))
                    {
                        // TODO: clean up null colors in some meaningful way other than silently deleting
                        continue;
                    }

                    colorDict.TryAdd(configNode.GetValue("name"), configNode);
                
                    var configOut = sanitizeColor(configNode);

                    obsoleteColorMap.TryAdd(configNode.GetValue("name"), configOut.Item2);
                    colorDictCached.TryAdd(configOut.Item2, configOut.Item1);
                }
            }
            else
            {
                colorMaster = new ConfigNode("COLOR_MASTER");
                foreach (var configNode in GameDatabase.Instance.GetConfigNodes("KSP_COLOR_PRESET"))
                {
                    if (configNode.GetValue("name").Contains("#") && configNode.GetValue("name").Length == 13)
                    {
                        colorDictCached.TryAdd(configNode.GetValue("name"), configNode);
                        continue;
                    }
                    if (configNode.GetColor("color").Equals(Color.clear))
                    {
                        // TODO: clean up null colors in some meaningful way other than silently deleting
                        Log.debug("TexturesUnlimited: Null color in load order");
                        continue;
                    }

                    colorDict.TryAdd(configNode.GetValue("name"), configNode);
                    
                    var configOut = sanitizeColor(configNode);
                    
                    obsoleteColorMap.TryAdd(configNode.GetValue("name"), configOut.Item2);
                    colorDictCached.TryAdd(configOut.Item2, configOut.Item1);
                }
            }

            if (primaryGroupMaster != null)
            {
                groupMaster = primaryGroupMaster;
                foreach (var configNode in groupMaster.GetNodes("PRESET_COLOR_GROUP"))
                {
                    if (!configNode.HasData || !configNode.HasValue("color")) continue;
                    if (!configNode.HasValue("name")) configNode.AddValue("name", "RECOVERED");
                    string writtenName = configNode.GetValue("name");
                    groupDict.TryAdd(writtenName, configNode);
                    string[] colors = configNode.GetValues("color");
                    List<string> cachedColors = new List<string>();
                    foreach (var colorName in colors)
                    {
                        if (colorName.Contains("#") && colorName.Length == 13)
                        {
                            ConfigNode representativeColor = colorDictCached[colorName];
                            // sanity check - should never return false here and true in foreach
                            if (representativeColor.GetValue("name") == colorName)
                            {
                                cachedColors.Add(colorName);
                            }
                            else
                            {
                                foreach (var (name, node) in colorDictCached)
                                {
                                    if (colorName != HSVRecoloringData.ConvertToHEXTwelve(new RecoloringDataPreset(node)))
                                        continue;
                                    cachedColors.Add(name);
                                    Log.debug("TexturesUnlimited: Contradictory sanitize result: colorName " + colorName +
                                                          " does not match with " +
                                                          representativeColor.GetValue("name") +
                                                          " but matches with " + name);
                                    Log.debug("TexturesUnlimited: Comparable HEX10s: (" +
                                              representativeColor.GetValue("name") +
                                              "): " +
                                              HSVRecoloringData.ConvertToHEXTwelve(
                                                  new RecoloringDataPreset(representativeColor)) + " (" +
                                              name +
                                              "): " + HSVRecoloringData.ConvertToHEXTwelve(
                                                  new RecoloringDataPreset(node)));
                                }
                            }
                        }
                        else
                        {
                            ConfigNode representativeColor = colorDict[colorName];
                            var sanitized = sanitizeColor(representativeColor);
                            if (colorDictCached.TryGetValue(sanitized.Item2, out ConfigNode sanitizedNode))
                            {
                                cachedColors.Add(sanitized.Item2);
                                continue;
                            }

                            Log.debug("TexturesUnlimited: Color preset group cannot match " + colorName +
                                      " to any sanitized of HEX12 " + sanitized.Item2);
                            // TODO: failed sanitized pull, silent delete?
                        }
                    }

                    configNode.RemoveValues("color");
                    foreach (var cachedColor in cachedColors)
                    {
                        configNode.AddValue("color", cachedColor);
                    }

                    groupDictCached.TryAdd(writtenName, configNode);
                }
            }
            else
            {
                groupMaster = new ConfigNode("GROUP_MASTER");
                foreach (var configNode in GameDatabase.Instance.GetConfigNodes("PRESET_COLOR_GROUP"))
                {
                    string writtenName = configNode.GetValue("name");
                    groupDict.TryAdd(writtenName, configNode);
                    string[] colors = configNode.GetValues("color");
                    List<string> cachedColors = new List<string>();
                    foreach (var colorName in colors)
                    {
                        if (colorName.Contains("#") && colorName.Length == 13)
                        {
                            ConfigNode representativeColor = colorDictCached[colorName];
                            // sanity check - should never return false here and true in foreach, throw exception if it does
                            if (representativeColor.GetValue("name") == colorName)
                            {
                                cachedColors.Add(colorName);
                            }
                            else
                            {
                                foreach (var (name, node) in colorDictCached)
                                {
                                    if (colorName != HSVRecoloringData.ConvertToHEXTwelve(new RecoloringDataPreset(node)))
                                        continue;
                                    cachedColors.Add(name);
                                    Log.exception("Contradictory sanitize result: colorName " + colorName +
                                                          " does not match with " +
                                                          representativeColor.GetValue("name") +
                                                          " but matches with " + name);
                                    Log.exception("Comparable HEX10s: (" +
                                                          representativeColor.GetValue("name") +
                                                          "): " +
                                                          HSVRecoloringData.ConvertToHEXTwelve(
                                                              new RecoloringDataPreset(representativeColor)) + " (" +
                                                          name +
                                                          "): " + HSVRecoloringData.ConvertToHEXTwelve(
                                                              new RecoloringDataPreset(node)));
                                }
                            }
                        }
                        else
                        {
                            ConfigNode representativeColor = colorDict[colorName];
                            var sanitized = sanitizeColor(representativeColor);
                            if (colorDictCached.TryGetValue(sanitized.Item2, out ConfigNode sanitizedNode))
                            {
                                cachedColors.Add(sanitized.Item2);
                                continue;
                            }

                            Log.debug("TexturesUnlimited: Color preset group cannot match " + colorName +
                                      " to any sanitized of HEX10 " + sanitized.Item2);
                            // TODO: failed sanitized pull, silent delete?
                        }
                    }

                    configNode.RemoveValues("color");
                    foreach (var cachedColor in cachedColors)
                    {
                        configNode.AddValue("color", cachedColor);
                    }

                    groupDictCached.TryAdd(writtenName, configNode);
                }
            }

            if (primaryLegacyMaster != null)
            {
                legacyMaster = primaryLegacyMaster;
                foreach (var configNode in legacyMaster.GetNodes("LEGACY_PAIRING"))
                {
                    obsoleteColorMap.TryAdd(configNode.GetValue("legacy"), configNode.GetValue("modern"));
                }
            }
            else
            {
                legacyMaster = new ConfigNode("LEGACY_MASTER");
            }

            colorMaster.ClearNodes();
            groupMaster.ClearNodes();
            legacyMaster.ClearNodes();
            foreach (var (name, node) in colorDictCached)
            {
                colorMaster.AddNode(node);
            }
            foreach (var (name, node) in groupDictCached)
            {
                groupMaster.AddNode(node);
            }
            foreach (var (legacy, modern) in obsoleteColorMap)
            {
                ConfigNode node = new ConfigNode("LEGACY_PAIRING");
                node.AddValue("legacy",  legacy);
                node.AddValue("modern", modern);
                legacyMaster.AddNode(node);
            }
            master.AddNode(colorMaster);
            master.AddNode(groupMaster);
            master.AddNode(legacyMaster);
            GameDatabase.CompileConfig(master);
            bool saved = master.Save(KSPUtil.ApplicationRootPath + "/GameData/000_TexturesUnlimited/ColorPresets.cfg", "KSP_MASTER");
            Log.log("TexturesUnlimited: MasterNode saved("+saved+") to " + (KSPUtil.ApplicationRootPath + "/GameData/000_TexturesUnlimited/ColorPresets.cfg") + " of count " + (colorMaster.CountNodes
                +"/"+ groupMaster.CountNodes));
        }
        
        internal static void loadPresetIntoGroup(RecoloringDataPreset preset, string group)
        {
            RecoloringDataPresetGroup colors;
            if (!presetGroups.TryGetValue(group, out colors))
            {
                colors = new RecoloringDataPresetGroup(group);
                presetGroups.Add(group, colors);
                presetGroupList.Add(colors);
            }
            if (!colors.colors.Contains(preset))
            {
                colors.colors.Add(preset);
            }
        }

        public static RecoloringDataPreset getColor(string name)
        {
            if (!presetColors.ContainsKey(name))
            {
                MonoBehaviour.print("ERROR: No Color data for name: " + name + " returning the first available color preset.");
                if (colorList.Count > 0)
                {
                    return colorList[0];
                }
                MonoBehaviour.print("ERROR: No preset colors defined, could not return a valid preset.");
                return new RecoloringDataPreset()
                {
                    colorHSV = uColor.gray,
                    metallic = 0,
                    specular = 0,
                    name = "ERROR",
                    title = "ERROR",
                };
            }
            return presetColors[name];
        }

        public static List<RecoloringDataPreset> getColorList() { return colorList; }

        public static List<RecoloringDataPreset> getColorList(string group)
        {
            RecoloringDataPresetGroup g;
            if (!presetGroups.TryGetValue(group, out g))
            {
                Log.error("No preset group found for name: " + group);
                return colorList;
            }            
            return g.colors;
        }

        public static List<RecoloringDataPresetGroup> getGroupList()
        {
            return presetGroupList;
        }

    }

}
