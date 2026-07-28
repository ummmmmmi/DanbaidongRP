namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Color Picker 的数值显示模式。
    /// </summary>
    public enum ColorPickerDebugMode
    {
        /// <summary>Disable the Color Picker.</summary>
        None,
        /// <summary>Display the red channel as a byte.</summary>
        Byte,
        /// <summary>Display all channels as bytes.</summary>
        Byte4,
        /// <summary>Display the red channel as a float.</summary>
        Float,
        /// <summary>Display all channels as floats.</summary>
        Float4,
    }
}
