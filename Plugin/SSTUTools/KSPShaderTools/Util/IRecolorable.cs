using UnityEngine;

namespace KSPShaderTools
{

    public interface IRecolorable
    {
        string[] getSectionNames();
        HSVRecoloringData[] getSectionColorsHSV(string name);
        RecoloringData[] getSectionColorsRGB(string name);
        TextureSet getSectionTexture(string name);
        void setSectionColorsHSV(string name, HSVRecoloringData[] colors);
        void setSectionColorsRGB(string name, RecoloringData[] colors);
    }

    public interface IPartTextureUpdated
    {
        void textureUpdated(Part part);
    }

    public interface IPartGeometryUpdated
    {
        void geometryUpdated(Part part);
    }

    public static class TextureCallbacks
    {
        public static void onTextureSetChanged(Part part)
        {
            IPartTextureUpdated[] iptu = part.GetComponents<IPartTextureUpdated>();
            int len = iptu.Length;
            for (int i = 0; i < len; i++)
            {
                iptu[i].textureUpdated(part);
            }
        }

        public static void onPartModelChanged(Part part)
        {
            IPartGeometryUpdated[] ipgu = part.GetComponents<IPartGeometryUpdated>();
            int len = ipgu.Length;
            for (int i = 0; i < len; i++)
            {
                ipgu[i].geometryUpdated(part);
            }
        }
    }
    

    /// <summary>
    ///  HSV color intercept
    /// </summary>
    public struct uColor
    {
        public float h;
        public float s;
        public float v;
        
        public uColor(float h, float s, float v)
        {
            this.h = h;
            this.s = s;
            this.v = v;
        }
        
        public static uColor white
        {
            get { return new uColor(0.0f, 0.0f, 1.0f); }
        }

        public static uColor black
        {
            get { return new uColor(0.0f, 0.0f, 0.0f); }
        }

        public static uColor gray
        {
            get { return uColor.fromShaderColor(Color.gray); }
        }
        
        /// <summary>
        /// Shorthand to convert a UnityEngine RGB color to HSV format and returns a new uColor
        /// </summary>
        /// <param name="color"></param>
        /// <returns>
        /// uColor (HSV)
        /// </returns>
        public static uColor fromShaderColor(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            uColor c = new uColor(h, s, v);
            return c;
        }
        
        /// <summary>
        /// Converts uColor to a UnityEngine RGBA color using UnityEngine.Color.HSVtoRGB with hdr false and A of 1
        /// </summary>
        /// <returns>
        /// UnityEngine.Color (RGBA)
        /// </returns>
        public static Color toShaderColor(uColor color)
        {
            Color c = new Color();
            c = Color.HSVToRGB(color.h, color.s, color.v, false);
            c.a = 1;
            return c;
        }
    }
    
    public struct HSVRecoloringData
    {
        public uColor color;
        public float specular;
        public float metallic;
        public float detail;
        
        public static bool isHSV = false;
        
        public HSVRecoloringData(string data)
        {
            string[] values = data.Split(',');
            int len = values.Length;
            if (len < 3)
            {
                Log.error("ERROR: Not enough data in: " + data + " to construct color values.");
                color = uColor.white;
                specular = 0;
                metallic = 0;
                detail = 0;
            }
            else
            {
                string hsv = values[0] + "," + values[1] + "," +  values[2];
                string specString = len > 3 ? values[3] : "0";
                string metalString = len > 4 ? values[4] : "0";
                string detailString = len > 5 ? values[5] : "0";
                color = Utils.HSVParseColor(hsv);
                specular = Utils.safeParseFloat(specString);
                metallic = Utils.safeParseFloat(metalString);
                detail = Utils.safeParseFloat(detailString);
            }
        }

        public HSVRecoloringData(uColor color, float spec, float metal)
        {
            this.color = color;
            specular = spec;
            metallic = metal;
            this.detail = 1;
        }
        
        public HSVRecoloringData(uColor color, float spec, float metal, float detail)
        {
            this.color = color;
            specular = spec;
            metallic = metal;
            this.detail = detail;
        }

        public HSVRecoloringData(HSVRecoloringData data)
        {
            color = data.color;
            specular = data.specular;
            metallic = data.metallic;
            detail = data.detail;
        }

        /// <summary>
        /// Converts this.color to a UnityEngine RGB color using UnityEngine.Color.HSVtoRGB with hdr false, appends specular as A, and returns
        /// </summary>
        /// <returns>
        /// UnityEngine.Color (RGBA)
        /// </returns>
        public Color getShaderColor()
        {
            Color c = new Color();
            c = Color.HSVToRGB(color.h, color.s, color.v, false);
            c.a = specular;
            return c;
        }
        

        public string getPersistentData()
        {
            return color.h + "," + color.s + "," + color.v + "," + specular + "," + metallic + "," + detail;
        }
        
        /// <summary>
        /// Parses saved persistence data, using floating-point color values as output by the getPersistentData() function.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static HSVRecoloringData ParsePersistence(string data)
        {
            return new HSVRecoloringData(data);
        }

        public static HSVRecoloringData ParseColorsBlockEntry(string data)
        {
            uColor color;
            float specular, metallic, detail;
            detail = 1;
            if (data.Contains(","))//CSV value, from floats
            {
                string[] values = data.Split(',');
                int len = values.Length;
                if (len < 3)
                {
                    Log.error("ERROR: Not enough data in: " + data + " to construct color values.");
                    color = uColor.white;
                    specular = 0;
                    metallic = 0;
                }
                else if (data.Contains("."))
                {
                    string hsv = values[0] + "," + values[1] + "," + values[2];
                    string specString = len > 3 ? values[3] : "0";
                    string metalString = len > 4 ? values[4] : "0";
                    string detailString = len > 5 ? values[5] : "1";
                    color = Utils.HSVParseColor(hsv);
                    specular = Utils.safeParseFloat(specString);
                    metallic = Utils.safeParseFloat(metalString);
                    detail = Utils.safeParseFloat(detailString);
                }
                else
                {
                    string hsv = values[0] + "," + values[1] + "," + values[2];
                    string specString = len > 3 ? values[3] : "0";
                    string metalString = len > 4 ? values[4] : "0";
                    string detailString = len > 5 ? values[5] : "255";
                    color = Utils.HSVParseColor(hsv);
                    specular = Utils.safeParseInt(specString) / 255f;
                    metallic = Utils.safeParseInt(metalString) / 255f;
                    detail = Utils.safeParseInt(detailString) / 255f;
                }
            }
            else //preset color, load from string value
            {
                RecoloringDataPreset preset = PresetColor.getColor(data);
                color = preset.colorHSV;
                specular = preset.specular;
                metallic = preset.metallic;
                detail = 1;
            }
            return new HSVRecoloringData(color, specular, metallic, detail);
        }
    }
    
    
    public struct RecoloringData
    {

        public Color color;
        public float specular;
        public float metallic;
        public float detail;

        public RecoloringData(string data)
        {
            string[] values = data.Split(',');
            int len = values.Length;
            if (len < 3)
            {
                Log.error("ERROR: Not enough data in: " + data + " to construct color values.");
                color = Color.white;
                specular = 0;
                metallic = 0;
                detail = 1;
            }
            else
            {
                string rgb = values[0] + "," + values[1] + "," + values[2] + ",1.0";
                string specString = len > 3 ? values[3] : "0";
                string metalString = len > 4 ? values[4] : "0";
                string detailString = len > 5 ? values[5] : "1";
                color = Utils.parseColor(rgb);
                specular = Utils.safeParseFloat(specString);
                metallic = Utils.safeParseFloat(metalString);
                detail = Utils.safeParseFloat(detailString);
            }
        }

        public RecoloringData(Color color, float spec, float metal)
        {
            this.color = color;
            specular = spec;
            metallic = metal;
            this.detail = 1;
        }

        public RecoloringData(Color color, float spec, float metal, float detail)
        {
            this.color = color;
            specular = spec;
            metallic = metal;
            this.detail = detail;
        }

        public RecoloringData(RecoloringData data)
        {
            color = data.color;
            specular = data.specular;
            metallic = data.metallic;
            detail = data.detail;
        }
        
        public Color getShaderColor()
        {
            color.a = specular;
            return color;
        }

        public string getPersistentData()
        {
            return color.r + "," + color.g + "," + color.b + "," + specular + "," + metallic + "," + detail;
        }

        /// <summary>
        /// Parses saved persistence data, using floating-point color values as output by the getPersistentData() function.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static RecoloringData ParsePersistence(string data)
        {
            return new RecoloringData(data);
        }

        /// <summary>
        /// Load a recoloring data instance from an input CSV string.  This method attempts intelligent parsing based on the values provided.
        /// If the value contains commas and periods, attempt to parse as floating point.  If the value contains commas only, attempt to parse
        /// as bytes.  If the value contains neither period nor comma, attempt to parse as a 'presetColor' name.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static RecoloringData ParseColorsBlockEntry(string data)
        {
            Color color;
            float specular, metallic, detail;
            detail = 1;
            if (data.Contains(","))//CSV value, parse from floats
            {
                string[] values = data.Split(',');
                int len = values.Length;
                if (len < 3)
                {
                    Log.error("ERROR: Not enough data in: " + data + " to construct color values.");
                    color = Color.white;
                    specular = 0;
                    metallic = 0;
                }
                else if (data.Contains("."))
                {
                    string rgb = values[0] + "," + values[1] + "," + values[2] + ",1.0";
                    string specString = len > 3 ? values[3] : "0";
                    string metalString = len > 4 ? values[4] : "0";
                    string detailString = len > 5 ? values[5] : "1";
                    color = Utils.parseColor(rgb);
                    specular = Utils.safeParseFloat(specString);
                    metallic = Utils.safeParseFloat(metalString);
                    detail = Utils.safeParseFloat(detailString);
                }
                else
                {
                    string rgb = values[0] + "," + values[1] + "," + values[2] + ",255";
                    string specString = len > 3 ? values[3] : "0";
                    string metalString = len > 4 ? values[4] : "0";
                    string detailString = len > 5 ? values[5] : "255";
                    color = Utils.parseColor(rgb);
                    specular = Utils.safeParseInt(specString) / 255f;
                    metallic = Utils.safeParseInt(metalString) / 255f;
                    detail = Utils.safeParseInt(detailString) / 255f;
                }
            }
            else //preset color, load from string value
            {
                RecoloringDataPreset preset = PresetColor.getColor(data);
                color = preset.colorRGB;
                specular = preset.specular;
                metallic = preset.metallic;
                detail = 1;
            }
            return new RecoloringData(color, specular, metallic, detail);
        }

    }

    /// <summary>
    /// Wraps a persistent data field in a PartModule/etc to support load/save operations for recoloring data in a consistent and encapsulated fashion.
    /// </summary>
    public class RecoloringHandler
    {
        private BaseField persistentDataField;

        private HSVRecoloringData[] colorData;

        public RecoloringHandler(BaseField persistentDataField)
        {
            this.persistentDataField = persistentDataField;
            int len = 3;
            colorData = new HSVRecoloringData[len];
            string data = this.persistentDataField.GetValue<string>(persistentDataField.host);
            if (string.IsNullOrEmpty(data))
            {
                for (int i = 0; i < len; i++)
                {
                    colorData[i] = new HSVRecoloringData(uColor.white, 0, 0, 1);
                }
            }
            else
            {
                string[] channelData = data.Split(';');
                for (int i = 0; i < len; i++)
                {
                    colorData[i] = HSVRecoloringData.ParsePersistence(channelData[i]);
                }
            }
        }

        public HSVRecoloringData getColorData(int index)
        {
            return colorData[index];
        }

        public HSVRecoloringData[] getColorData()
        {
            return colorData;
        }

        public void setColorData(HSVRecoloringData[] data)
        {
            this.colorData = data;
            save();
        }

        public void save()
        {
            int len = colorData.Length;
            string data = "";
            for (int i = 0; i < len; i++)
            {
                if (i > 0)
                {
                    data = data + ";";
                }
                data = data + colorData[i].getPersistentData();
            }
            persistentDataField.SetValue(data, persistentDataField.host);
        }

    }

}
