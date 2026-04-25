using KSPShaderTools.Settings;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace KSPShaderTools
{
    
    /// Considering default colorspace to be HSV in cases where setting including both may not make sense/work
    public class CraftRecolorGUI : MonoBehaviour
    {
        private static int graphWidth = 400;
        private static int graphHeight = 540;
        private static int sectionHeight = 100;
        private static float windowX = -1;
        private static float windowY = -1;
        private static int id;
        private static Rect windowRect = new Rect(Screen.width - 500, 40, graphWidth, graphHeight);
        private static Vector2 scrollPos;
        private static Vector2 presetColorScrollPos;
        private static GUIStyle nonWrappingLabelStyle = null;

        private List<ModuleRecolorData> moduleRecolorData = new List<ModuleRecolorData>();
        
        internal Action guiCloseAction;

        private SectionRecolorDataHSV sectionDataHSV;
        private SectionRecolorDataRGB sectionDataRGB;
        /// <summary>
        /// Defines which ModuleRecolorData is used for UI setup and callbacks
        /// </summary>
        private int moduleIndex = -1;
        /// <summary>
        /// Defines which subsection of the selected module is currently being edited
        /// </summary>
        private int sectionIndex = -1;
        /// <summary>
        /// Defines which column the user is editing - main/second/detail
        /// </summary>
        private int colorIndex = -1;
        private string rStr, gStr, bStr, hStr, sStr, vStr, aStr, mStr, dStr;//string caches of color values//TODO -- set initial state when a section color is selected
        private static HSVRecoloringData editingColorHSV;
        private static HSVRecoloringData[] storedPatternHSV;
        private static HSVRecoloringData storedColorHSV;
        private static RecoloringData editingColorRGB;
        private static RecoloringData[] storedPatternRGB;
        private static RecoloringData storedColorRGB;
        /// <summary>
        /// The name of the currently selected preset color group
        /// </summary>
        private static string groupName = "FULL";
        /// <summary>
        /// Index into the list of groups for the currently selected group
        /// </summary>
        private static int groupIndex = 0;

        private static bool scrollLock = false;

        public static Part openPart;

        public void Awake()
        {
            id = GetInstanceID();
            graphWidth = TUGameSettings.RecolorGUIWidth;// TexturesUnlimitedLoader.recolorGUIWidth;
            graphHeight = TUGameSettings.RecolorGUIHeight;// TexturesUnlimitedLoader.recolorGUITotalHeight;
            sectionHeight = TUGameSettings.RecolorGUITopHeight;// TexturesUnlimitedLoader.recolorGUISectionHeight;
            if (windowX == -1)
            {
                windowRect.x = Screen.width - (graphWidth + 100);
            }
            else
            {
                windowRect.x = windowX;
                windowRect.y = windowY;
            }            
        }

        internal void openGUIPart(Part part)
        {
            windowRect.width = graphWidth;
            windowRect.height = graphHeight;
            if (part != openPart)
            {
                moduleIndex = -1;
                sectionIndex = -1;
                colorIndex = -1;
            }
            ControlTypes controls = ControlTypes.ALLBUTCAMERAS;
            controls = controls & ~ControlTypes.TWEAKABLES;
            InputLockManager.SetControlLock(controls, "SSTURecolorGUILock");
            setupForPart(part);
            if (moduleIndex < 0 || sectionIndex < 0)
            {
                findFirstRecolorable(out moduleIndex, out sectionIndex);
                colorIndex = 0;
            }
            if (colorIndex < 0)
            {
                colorIndex = 0;
            }
            setupSectionDataHSV(moduleRecolorData[moduleIndex].sectionDataHSV[sectionIndex], colorIndex);
            setupSectionDataRGB(moduleRecolorData[moduleIndex].sectionDataRGB[sectionIndex], colorIndex);
            openPart = part;
        }

        /// <summary>
        /// To be called from the external 'GuiCloseAction' delegate.
        /// </summary>
        internal void closeGui()
        {
            closeSectionGUI();
            moduleRecolorData.Clear();
            sectionDataHSV = null;
            sectionDataRGB = null;
            openPart = null;
            InputLockManager.RemoveControlLock("SSTURecolorGUILock");
            InputLockManager.RemoveControlLock("SSTURecolorGUILock2");
            colorIndex = -1;
            moduleIndex = -1;
            sectionIndex = -1;
        }

        internal void refreshGui(Part part)
        {
            if (part != openPart) { return; }

            moduleRecolorData.Clear();
            setupForPart(part);

            int len = moduleRecolorData.Count;
            if (moduleIndex >= len)
            {
                findFirstRecolorable(out moduleIndex, out sectionIndex);
            }
            len = moduleRecolorData[moduleIndex].sectionDataHSV.Length;
            if (sectionIndex >= len)
            {
                findFirstRecolorable(moduleIndex, out moduleIndex, out sectionIndex);
            }

            ModuleRecolorData mrd = moduleRecolorData[moduleIndex];
            SectionRecolorDataHSV srd = mrd.sectionDataHSV[sectionIndex];
            if (!srd.recoloringSupported())
            {
                findFirstRecolorable(out moduleIndex, out sectionIndex);
            }

            setupSectionDataHSV(moduleRecolorData[moduleIndex].sectionDataHSV[sectionIndex], colorIndex);
            setupSectionDataRGB(moduleRecolorData[moduleIndex].sectionDataRGB[sectionIndex], colorIndex);
        }

        private void setupForPart(Part part)
        {
            List<IRecolorable> mods = part.FindModulesImplementing<IRecolorable>();
            foreach (IRecolorable mod in mods)
            {
                ModuleRecolorData data = new ModuleRecolorData((PartModule)mod, mod);
                moduleRecolorData.Add(data);
            }
        }

        private void findFirstRecolorable(out int module, out int section)
        {
            int len = moduleRecolorData.Count;
            ModuleRecolorData mrd;
            for (int i = 0; i < len; i++)
            {
                mrd = moduleRecolorData[i];
                int len2 = mrd.sectionDataHSV.Length;
                SectionRecolorDataHSV srd;
                for (int k = 0; k < len2; k++)
                {
                    srd = mrd.sectionDataHSV[k];
                    if (srd.recoloringSupported())
                    {
                        module = i;
                        section = k;
                        return;
                    }
                }
            }
            Log.error("ERROR: Could not locate recolorable section for part: " + openPart);
            module = 0;
            section = 0;
        }

        private void findFirstRecolorable(int moduleStart, out int module, out int section)
        {
            module = moduleStart;
            if (moduleStart < moduleRecolorData.Count)
            {
                ModuleRecolorData mrd = moduleRecolorData[moduleStart];
                int len = mrd.sectionDataHSV.Length;
                SectionRecolorDataHSV srd;
                for (int i = 0; i < len; i++)
                {
                    srd = mrd.sectionDataHSV[i];
                    if (srd.recoloringSupported())
                    {
                        //found section in current module that supports recoloring, return it
                        section = i;
                        return;
                    }
                }
            }
            //if recolorable could not be found in current module selection, default to searching entire part
            findFirstRecolorable(out module, out section);
        }

        public void OnGUI()
        {
            //apparently trying to initialize this during OnAwake/etc fails, as unity is dumb and requires that it be done during an OnGUI call
            //serious -- you cant even access the GUI.skin except in OnGUi...
            if (nonWrappingLabelStyle == null)
            {
                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.wordWrap = false;
                nonWrappingLabelStyle = style;
            }
            windowRect = GUI.Window(id, windowRect, drawWindow, "Part Recoloring");
            windowX = windowRect.x;
            windowY = windowRect.y;
        }

        private void drawWindow(int id)
        {
            bool lockedScroll = false;
            if (windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
            {
                lockedScroll = true;
                scrollLock = true;
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, "SSTURecolorGUILock2");
            }
            UnityEngine.GUILayout.BeginVertical();
            drawSectionSelectionArea();
            drawSectionRecoloringArea();
            drawPresetColorArea();
            if (GUILayout.Button("Close"))
            {
                guiCloseAction();//call the method in SSTULauncher to close this GUI
            }
            GUILayout.EndVertical();
            GUI.DragWindow();
            if (!lockedScroll && scrollLock)
            {
                InputLockManager.RemoveControlLock("SSTURecolorGUILock2");
            }
        }

        private void setupSectionDataHSV(SectionRecolorDataHSV section, int colorIndex)
        {
            this.sectionDataHSV = section;
            this.colorIndex = colorIndex;
            if (section.colorsHSV == null) { return; }
            editingColorHSV = sectionDataHSV.colorsHSV[colorIndex];
            hStr = (editingColorHSV.color.h * 360f).ToString("F0");
            sStr = (editingColorHSV.color.s * 100f).ToString("F0");
            vStr = (editingColorHSV.color.v * 100f).ToString("F0");
            aStr = (editingColorHSV.specular * 255f).ToString("F0");
            mStr = (editingColorHSV.metallic * 255f).ToString("F0");
            dStr = (editingColorHSV.detail * 100).ToString("F0");
        }

        private void setupSectionDataRGB(SectionRecolorDataRGB section, int colorIndex)
        {
            this.sectionDataRGB = section;
            this.colorIndex = colorIndex;
            if (section.colorsRGB == null) {return;}
            editingColorRGB = sectionDataRGB.colorsRGB[colorIndex];
            rStr = (editingColorRGB.color.r * 255f).ToString("F0");
            gStr = (editingColorRGB.color.g * 255f).ToString("F0");
            bStr = (editingColorRGB.color.b * 255f).ToString("F0");
            aStr = (editingColorRGB.specular * 255f).ToString("F0");
            mStr = (editingColorRGB.metallic * 255f).ToString("F0");
            dStr = (editingColorRGB.detail * 100).ToString("F0");
        }

        private void closeSectionGUI()
        {
            sectionDataHSV = null;
            sectionDataRGB = null;
            editingColorHSV = new HSVRecoloringData(uColor.white, 0, 0, 1);
            editingColorRGB = new RecoloringData(Color.white, 0, 0, 1);
            rStr = gStr = bStr = aStr = mStr = dStr = "255";
            colorIndex = 0;
        }

        // Consider RGB as default here against standard due to GUI colors
        private void drawSectionSelectionArea()
        {
            GUILayout.BeginHorizontal();
            Color old = GUI.color;
            float buttonWidth = 70;
            float scrollWidth = 40;
            float sectionTitleWidth = graphWidth - scrollWidth - buttonWidth * 3 - scrollWidth;
            GUILayout.Label("Section", GUILayout.Width(sectionTitleWidth));
            GUI.color = colorIndex == 0 ? Color.red : old;
            GUILayout.Label("Main", GUILayout.Width(buttonWidth));
            GUI.color = colorIndex == 1 ? Color.red : old;
            GUILayout.Label("Second", GUILayout.Width(buttonWidth));
            GUI.color = colorIndex == 2 ? Color.red : old;
            GUILayout.Label("Detail", GUILayout.Width(buttonWidth));
            GUI.color = old;
            GUILayout.EndHorizontal();
            Color guiColor = old;
            scrollPos = GUILayout.BeginScrollView(scrollPos, false, true, GUILayout.Height(sectionHeight));
            int len = moduleRecolorData.Count;
            for (int i = 0; i < len; i++)
            {
                int len2 = moduleRecolorData[i].sectionDataRGB.Length;
                for (int k = 0; k < len2; k++)
                {
                    if (!moduleRecolorData[i].sectionDataRGB[k].recoloringSupported())
                    {
                        continue;
                    }
                    GUILayout.BeginHorizontal();
                    if ( k == sectionIndex && i == moduleIndex )
                    {
                        GUI.color = Color.red;
                    }
                    GUILayout.Label(moduleRecolorData[i].sectionDataRGB[k].sectionName, GUILayout.Width(sectionTitleWidth));
                    for (int m = 0; m < 3; m++)
                    {
                        int mask = 1 << m;
                        if (moduleRecolorData[i].sectionDataRGB[k].channelSupported(mask))
                        {
                            guiColor = (moduleRecolorData[i].sectionDataRGB[k].colorsRGB[m].color);
                            guiColor.a = 1;
                            GUI.color = guiColor;
                            if (GUILayout.Button("Recolor", GUILayout.Width(70)))
                            {
                                moduleIndex = i;
                                sectionIndex = k;
                                colorIndex = m;
                                setupSectionDataHSV(moduleRecolorData[i].sectionDataHSV[k], m);
                                setupSectionDataRGB(moduleRecolorData[i].sectionDataRGB[k], m);
                            }
                        }
                        else
                        {
                            GUILayout.Label("", GUILayout.Width(70));
                        }
                    }
                    GUI.color = old;
                    GUILayout.EndHorizontal();
                }
            }
            GUI.color = old;
            GUILayout.EndScrollView();
        }

        private void drawSectionRecoloringArea()
        {
            if (HSVRecoloringData.isHSV && sectionDataHSV == null)
            {
                return;
            }

            if (!HSVRecoloringData.isHSV && sectionDataRGB == null)
            {
                return;
            }

            bool updated = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Editing: ", GUILayout.Width(60));
            GUILayout.Label(sectionDataHSV.sectionName);
            GUILayout.Label(getSectionLabel(colorIndex) + " Color");
            
            // Default state to isHSV==0, hrBT==RGB
            if (GUILayout.Button(HSVRecoloringData.isHSV ? "HSV" : "RGB", GUILayout.Width(60)))
            {
                HSVRecoloringData.isHSV ^= true;
            }

            GUILayout.FlexibleSpace(); //to force everything to the left instead of randomly spaced out, while still allowing dynamic length adjustments
            GUILayout.EndHorizontal();

            // Could be done better, but this avoids dozens of isHSV checks within the gui block.
            // TODO: this also makes it so that RGB and HSV store separate colors and palettes. Whether that's desirable is a decision I'll leave others to. If not, simply merge xxxHSV and xxxRGB fields and standardize the back-end to one.
            if (HSVRecoloringData.isHSV)
            {
                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Hue", ref editingColorHSV.color.h, ref hStr, sectionDataHSV.colorSupported(), 360, 1))
                {
                    updated = true;
                }

                if (GUILayout.Button("Load Pattern", GUILayout.Width(120)))
                {
                    sectionDataHSV.colorsHSV[0] = storedPatternHSV[0];
                    sectionDataHSV.colorsHSV[1] = storedPatternHSV[1];
                    sectionDataHSV.colorsHSV[2] = storedPatternHSV[2];
                    editingColorHSV = sectionDataHSV.colorsHSV[colorIndex];
                    updated = true;
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Saturation", ref editingColorHSV.color.s, ref sStr, sectionDataHSV.colorSupported(),
                        100, 1))
                {
                    updated = true;
                }

                if (GUILayout.Button("Store Pattern", GUILayout.Width(120)))
                {
                    storedPatternHSV = new HSVRecoloringData[3];
                    storedPatternHSV[0] = sectionDataHSV.colorsHSV[0];
                    storedPatternHSV[1] = sectionDataHSV.colorsHSV[1];
                    storedPatternHSV[2] = sectionDataHSV.colorsHSV[2];
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Value", ref editingColorHSV.color.v, ref vStr, sectionDataHSV.colorSupported(), 100,
                        1))
                {
                    updated = true;
                }

                if (GUILayout.Button("Load Color", GUILayout.Width(120)))
                {
                    editingColorHSV = storedColorHSV;
                    updated = true;
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Specular", ref editingColorHSV.specular, ref aStr, sectionDataHSV.specularSupported(),
                        255, 1))
                {
                    updated = true;
                }

                if (GUILayout.Button("Store Color", GUILayout.Width(120)))
                {
                    storedColorHSV = editingColorHSV;
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (sectionDataHSV.metallicSupported())
                {
                    if (drawColorInputLine("Metallic", ref editingColorHSV.metallic, ref mStr, true, 255, 1))
                    {
                        updated = true;
                    }
                }
                else if (sectionDataHSV.hardnessSupported())
                {
                    if (drawColorInputLine("Hardness", ref editingColorHSV.metallic, ref mStr, true, 255, 1))
                    {
                        updated = true;
                    }
                }
                else
                {
                    if (drawColorInputLine("Metallic", ref editingColorHSV.metallic, ref mStr, false, 255, 1))
                    {
                        updated = true;
                    }
                }

                if (GUILayout.Button("<", GUILayout.Width(20)))
                {
                    groupIndex--;
                    List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                    if (groupIndex < 0)
                    {
                        groupIndex = gs.Count - 1;
                    }

                    groupName = gs[groupIndex].name;
                }

                GUILayout.Label("Palette", GUILayout.Width(70));
                if (GUILayout.Button(">", GUILayout.Width(20)))
                {
                    groupIndex++;
                    List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                    if (groupIndex >= gs.Count)
                    {
                        groupIndex = 0;
                    }

                    groupName = gs[groupIndex].name;
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Detail %", ref editingColorHSV.detail, ref dStr, true, 100, 5))
                {
                    updated = true;
                }

                GUILayout.Label(groupName, GUILayout.Width(120));
                GUILayout.EndHorizontal();

                if (updated)
                {
                    sectionDataHSV.colorsHSV[colorIndex] = editingColorHSV;
                    sectionDataHSV.updateColors();
                }
            }
            
            else
            {
                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Red", ref editingColorRGB.color.r, ref rStr, sectionDataHSV.colorSupported(), 255, 1))
                {
                    updated = true;
                }
            
                if (GUILayout.Button("Load Pattern", GUILayout.Width(120)))
                {
                    sectionDataHSV.colorsRGB[0] = storedPatternRGB[0];
                    sectionDataHSV.colorsRGB[1] = storedPatternRGB[1];
                    sectionDataHSV.colorsRGB[2] = storedPatternRGB[2];
                    editingColorRGB = sectionDataHSV.colorsRGB[colorIndex];
                    updated = true;
                }
            
                GUILayout.EndHorizontal();
            
                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Green", ref editingColorRGB.color.g, ref gStr, sectionDataHSV.colorSupported(),
                        255, 1))
                {
                    updated = true;
                }
            
                if (GUILayout.Button("Store Pattern", GUILayout.Width(120)))
                {
                    storedPatternRGB = new RecoloringData[3];
                    storedPatternRGB[0] = sectionDataRGB.colorsRGB[0];
                    storedPatternRGB[1] = sectionDataRGB.colorsRGB[1];
                    storedPatternRGB[2] = sectionDataRGB.colorsRGB[2];
                }
            
                GUILayout.EndHorizontal();
            
                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Blue", ref editingColorRGB.color.b, ref bStr, sectionDataRGB.colorSupported(), 255,
                        1))
                {
                    updated = true;
                }
            
                if (GUILayout.Button("Load Color", GUILayout.Width(120)))
                {
                    editingColorRGB = storedColorRGB;
                    updated = true;
                }
            
                GUILayout.EndHorizontal();
            
                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Specular", ref editingColorRGB.specular, ref aStr, sectionDataRGB.specularSupported(),
                        255, 1))
                {
                    updated = true;
                }
            
                if (GUILayout.Button("Store Color", GUILayout.Width(120)))
                {
                    storedColorRGB = editingColorRGB;
                }
            
                GUILayout.EndHorizontal();
            
                GUILayout.BeginHorizontal();
                if (sectionDataRGB.metallicSupported())
                {
                    if (drawColorInputLine("Metallic", ref editingColorRGB.metallic, ref mStr, true, 255, 1))
                    {
                        updated = true;
                    }
                }
                else if (sectionDataRGB.hardnessSupported())
                {
                    if (drawColorInputLine("Hardness", ref editingColorRGB.metallic, ref mStr, true, 255, 1))
                    {
                        updated = true;
                    }
                }
                else
                {
                    if (drawColorInputLine("Metallic", ref editingColorRGB.metallic, ref mStr, false, 255, 1))
                    {
                        updated = true;
                    }
                }
            
                if (GUILayout.Button("<", GUILayout.Width(20)))
                {
                    groupIndex--;
                    List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                    if (groupIndex < 0)
                    {
                        groupIndex = gs.Count - 1;
                    }
            
                    groupName = gs[groupIndex].name;
                }
            
                GUILayout.Label("Palette", GUILayout.Width(70));
                if (GUILayout.Button(">", GUILayout.Width(20)))
                {
                    groupIndex++;
                    List<RecoloringDataPresetGroup> gs = PresetColor.getGroupList();
                    if (groupIndex >= gs.Count)
                    {
                        groupIndex = 0;
                    }
            
                    groupName = gs[groupIndex].name;
                }
            
                GUILayout.EndHorizontal();
            
                GUILayout.BeginHorizontal();
                if (drawColorInputLine("Detail %", ref editingColorRGB.detail, ref dStr, true, 100, 5))
                {
                    updated = true;
                }
            
                GUILayout.Label(groupName, GUILayout.Width(120));
                GUILayout.EndHorizontal();
            
                if (updated)
                {
                    sectionDataRGB.colorsRGB[colorIndex] = editingColorRGB;
                    sectionDataRGB.updateColors();
                }
            }
        }

        private void drawPresetColorArea()
        {
            if (sectionDataHSV == null)
            {
                return;
            }
            GUILayout.Label("Select a preset color: ");
            presetColorScrollPos = GUILayout.BeginScrollView(presetColorScrollPos, false, true);
            bool update = false;
            Color old = GUI.color;
            Color guiColor = old;
            List<RecoloringDataPreset> presetColors = PresetColor.getColorList(groupName);
            int len = presetColors.Count;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < len; i++)
            {
                if (i > 0 && i % 2 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                GUILayout.Label(presetColors[i].title, nonWrappingLabelStyle, GUILayout.Width(115));
                guiColor = uColor.toShaderColor(presetColors[i].colorHSV);
                guiColor.a = 1f;
                GUI.color = guiColor;
                if (GUILayout.Button("Select", GUILayout.Width(55)))
                {
                    editingColorHSV = presetColors[i].getHSVRecoloringData();
                    hStr = (editingColorHSV.color.h * 255f).ToString("F0");
                    sStr = (editingColorHSV.color.s * 255f).ToString("F0");
                    vStr = (editingColorHSV.color.v * 255f).ToString("F0");
                    aStr = (editingColorHSV.specular * 255f).ToString("F0");
                    mStr = (editingColorHSV.metallic * 255f).ToString("F0");
                    //dStr = (editingColor.detail * 100f).ToString("F0");//leave detail mult as pre-specified value (user/config); it does not pull from preset colors at all
                    update = true;
                }
                GUI.color = old;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUI.color = old;
            if (sectionDataHSV.colorsHSV != null)
            {
                sectionDataHSV.colorsHSV[colorIndex] = editingColorHSV;
                if (update)
                {
                    sectionDataHSV.updateColors();
                }
            }
        }

        private bool drawColorInputLine(string label, ref float val, ref string sVal, bool enabled, float mult, float max)
        {
            if (!enabled)
            {
                GUILayout.Label("", GUILayout.Width(60 + 120 + 60));
                return false;
            }
            //TODO -- text input validation for numbers only -- http://answers.unity3d.com/questions/18736/restrict-characters-in-guitextfield.html
            // also -- https://forum.unity3d.com/threads/text-field-for-numbers-only.106418/
            GUILayout.Label(label, GUILayout.Width(60));
            bool updated = false;
            float result = val;
            result = GUILayout.HorizontalSlider(val, 0, max, GUILayout.Width(120));
            if (result != val)
            {
                val = result;
                sVal = (val * mult).ToString("F0");
                updated = true;
            }
            string textOutput = GUILayout.TextField(sVal, 3, GUILayout.Width(60));
            if (sVal != textOutput)
            {
                sVal = textOutput;
                int iVal;
                if (int.TryParse(textOutput, out iVal))
                {
                    val = iVal / mult;
                    updated = true;
                }
            }
            return updated;
        }

        private string getSectionLabel(int index)
        {
            switch (index)
            {
                case 0:
                    return "Main";
                case 1:
                    return "Secondary";
                case 2:
                    return "Detail";
                default:
                    return "Unknown";
            }
        }

    }

    public class ModuleRecolorData
    {
        public PartModule module;//must implement IRecolorable
        public IRecolorable iModule;//interface version of module
        public SectionRecolorDataHSV[] sectionDataHSV;
        public SectionRecolorDataRGB[] sectionDataRGB;

        public ModuleRecolorData(PartModule module, IRecolorable iModule)
        {
            this.module = module;
            this.iModule = iModule;
            string[] names = iModule.getSectionNames();
            int len = names.Length;
            sectionDataHSV = new SectionRecolorDataHSV[len];
            sectionDataRGB = new SectionRecolorDataRGB[len];
            for (int i = 0; i < len; i++)
            {
                sectionDataHSV[i] = new SectionRecolorDataHSV(iModule, names[i], iModule.getSectionColorsHSV(names[i]), iModule.getSectionTexture(names[i]));
                sectionDataRGB[i] = new SectionRecolorDataRGB(iModule, names[i], iModule.getSectionColorsRGB(names[i]), iModule.getSectionTexture(names[i]));
            }
        }
    }

    public class ClonedSectionRecolorData
    {
        public readonly IRecolorable owner;
        public readonly string sectionName;
        public HSVRecoloringData[] colors;
        private TextureSet sectionTexture;
        
        public ClonedSectionRecolorData(IRecolorable owner, string name, HSVRecoloringData[] colors, TextureSet set)
        {
            this.owner = owner;
            this.sectionName = name;
            this.colors = colors;
            this.sectionTexture = set;
            if (colors == null)
            {
                //owners may return null for set and/or colors if recoloring is unsupported
                set = sectionTexture = null;
            }
            //MonoBehaviour.print("Created section recolor data with texture set: " + set+" for section: "+name);
            if (set != null)
            {
                //MonoBehaviour.print("Set name: " + set.name + " :: " + set.title + " recolorable: " + set.supportsRecoloring);
            }
            else
            {
                Log.error("Set was null while setting up recoloring section for: "+name);
            }
        }

        public void updateColors()
        {
            
        }
    }

    public class SectionRecolorDataHSV
    {
        public readonly IRecolorable owner;
        public readonly string sectionName;
        public HSVRecoloringData[] colorsHSV;
        public RecoloringData[] colorsRGB;
        private TextureSet sectionTexture;

        public SectionRecolorDataHSV(IRecolorable owner, string name, HSVRecoloringData[] colors, TextureSet set)
        {
            this.owner = owner;
            this.sectionName = name;
            this.colorsHSV = colors;
            this.sectionTexture = set;
            if (colors == null)
            {
                //owners may return null for set and/or colors if recoloring is unsupported
                set = sectionTexture = null;
            }
            //MonoBehaviour.print("Created section recolor data with texture set: " + set+" for section: "+name);
            if (set != null)
            {
                //MonoBehaviour.print("Set name: " + set.name + " :: " + set.title + " recolorable: " + set.supportsRecoloring);
            }
            else
            {
                Log.error("Set was null while setting up recoloring section for: "+name);
            }
        }

        public void updateColors()
        {
            owner.setSectionColorsHSV(sectionName, colorsHSV);
        }

        public bool recoloringSupported()
        {
            if (sectionTexture == null) { return false; }
            return sectionTexture.supportsRecoloring;
        }

        public bool colorSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 1) != 0;
        }

        public bool channelSupported(int mask)
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.recolorableChannelMask & mask) != 0;
        }

        public bool specularSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 2) != 0;
        }

        public bool metallicSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 4) != 0;
        }

        public bool hardnessSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 8) != 0;
        }

    }

    public class SectionRecolorDataRGB
    {
        public readonly IRecolorable owner;
        public readonly string sectionName;
        public RecoloringData[] colorsRGB;
        private TextureSet sectionTexture;

        public SectionRecolorDataRGB(IRecolorable owner, string name, RecoloringData[] colors, TextureSet set)
        {
            this.owner = owner;
            this.sectionName = name;
            this.colorsRGB = colors;
            this.sectionTexture = set;
            if (colors == null)
            {
                //owners may return null for set and/or colors if recoloring is unsupported
                set = sectionTexture = null;
            }
            //MonoBehaviour.print("Created section recolor data with texture set: " + set+" for section: "+name);
            if (set != null)
            {
                //MonoBehaviour.print("Set name: " + set.name + " :: " + set.title + " recolorable: " + set.supportsRecoloring);
            }
            else
            {
                Log.error("Set was null while setting up recoloring section for: "+name);
            }
        }

        // TODO: setting setSectionColors will likely cause an error.
        public void updateColors()
        {
            owner.setSectionColorsRGB(sectionName, colorsRGB);
        }

        public bool recoloringSupported()
        {
            if (sectionTexture == null) { return false; }
            return sectionTexture.supportsRecoloring;
        }

        public bool colorSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 1) != 0;
        }

        public bool channelSupported(int mask)
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.recolorableChannelMask & mask) != 0;
        }

        public bool specularSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 2) != 0;
        }

        public bool metallicSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 4) != 0;
        }

        public bool hardnessSupported()
        {
            if (sectionTexture == null) { return false; }
            return (sectionTexture.featureMask & 8) != 0;
        }

    }
    
}

    