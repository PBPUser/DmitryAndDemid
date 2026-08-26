using DmitryAndDemid.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;


namespace DmitryAndDemid.Data;


/// <summary>
/// Properties to load texture, which loads from BitPackage or Json
/// </summary>
public class TextureLoadingProperties
{
    /// <summary>
    /// Divider for texture resolution in low texture quality
    /// </summary>
    [JsonInclude] public int LowQualityDivider = 1;
    /// <summary>
    /// Divider for texture resolution in middle texture quality
    /// </summary>
    [JsonInclude] public int MidQualityDivider = 1;
    /// <summary>
    /// Resize Texture when scaling lower than FullResolutionScaling by dividing their resolution by FullResolutionScaling value and multipling it with Runtime.CurrentRuntime.Scale
    /// </summary>
    [JsonInclude] public bool MatchGameResolutionScaling = false;
    /// <summary>
    /// Usage Described in summary of MatchGameResolutionScaling summary
    /// </summary>
    [JsonInclude] public float FullResolutionScaling = 1;
    /// <summary>
    /// Describes game when load this texture, if empty, it loads everytime
    /// </summary>
    [JsonInclude] public string TextureLoadGroup = "";

    public static TextureLoadingProperties LoadFromBitPackage(ref BitPackage package)
    {
        TextureLoadingProperties tlp = new TextureLoadingProperties();
        tlp.LowQualityDivider = (int)package.ReadVarULong();
        tlp.MidQualityDivider = (int)package.ReadVarULong();
        tlp.MatchGameResolutionScaling = package.ReadByte() == 0;
        tlp.FullResolutionScaling = package.ReadFloat();
        tlp.TextureLoadGroup = package.ReadString();
        return tlp;
    }

    /// <summary>
    /// this method saves class into bitpackage in order that used in LoadFromBitPackage
    /// </summary>
    public void SaveToBitPackage(ref BitPackage bitPackage)
    {
        bitPackage.WriteVarULong((ulong)LowQualityDivider);
        bitPackage.WriteVarULong((ulong)MidQualityDivider);
        bitPackage.WriteByte((byte)(MatchGameResolutionScaling == true ? 1 : 0));
        bitPackage.WriteFloat(FullResolutionScaling);
        bitPackage.WriteString(TextureLoadGroup);
    }

}
