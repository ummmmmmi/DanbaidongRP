#ifndef UNIVERSAL_DEBUG_COLOR_PICKER_INCLUDED
#define UNIVERSAL_DEBUG_COLOR_PICKER_INCLUDED

#define COLOR_PICKER_MODE_NONE 0
#define COLOR_PICKER_MODE_BYTE 1
#define COLOR_PICKER_MODE_BYTE4 2
#define COLOR_PICKER_MODE_FLOAT 3
#define COLOR_PICKER_MODE_FLOAT4 4

#define COLOR_PICKER_FONT_WIDTH 16
#define COLOR_PICKER_FONT_HEIGHT 16
#define COLOR_PICKER_FONT_COUNT_X 16
#define COLOR_PICKER_FONT_COUNT_Y 8
#define COLOR_PICKER_FONT_ASCII_START 32
#define COLOR_PICKER_FONT_ADVANCE 10

TEXTURE2D(_ColorPickerDebugFont);
TEXTURE2D_X(_ColorPickerDebugTexture);

int _ColorPickerMode;
int _ColorPickerApplyDebug;
float3 _ColorPickerFontColor;
float4 _ColorPickerMousePixelCoord;
float4 _ColorPickerScreenSize;

bool ShouldFlipColorPickerCoordinates()
{
#if UNITY_UV_STARTS_AT_TOP
    return _ProjectionParams.x > 0.0;
#else
    return _ProjectionParams.x < 0.0;
#endif
}

float GetColorPickerFontScale()
{
    return max(_ColorPickerScreenSize.y / 1080.0, 1.0);
}

int GetColorPickerFontAdvance()
{
    return max(1, (int)round(COLOR_PICKER_FONT_ADVANCE * GetColorPickerFontScale()));
}

int GetColorPickerLineHeight()
{
    return max(1, (int)round(COLOR_PICKER_FONT_HEIGHT * GetColorPickerFontScale()));
}

void DrawColorPickerCharacter(uint asciiValue, int2 currentCoord, inout int2 textCoord, inout float3 color, int direction)
{
    float fontScale = GetColorPickerFontScale();
    int2 localScreenCoord = currentCoord - textCoord;
    int2 scaledCharacterSize = int2(ceil(float2(COLOR_PICKER_FONT_WIDTH, COLOR_PICKER_FONT_HEIGHT) * fontScale));
    if (all(localScreenCoord >= 0) && all(localScreenCoord < scaledCharacterSize))
    {
        uint2 localCoord = min(uint2(float2(localScreenCoord) / fontScale), uint2(COLOR_PICKER_FONT_WIDTH - 1, COLOR_PICKER_FONT_HEIGHT - 1));
        localCoord.y = COLOR_PICKER_FONT_HEIGHT - localCoord.y;
        asciiValue -= COLOR_PICKER_FONT_ASCII_START;
        uint2 asciiCoord = uint2(asciiValue % COLOR_PICKER_FONT_COUNT_X, asciiValue / COLOR_PICKER_FONT_COUNT_X);
        uint2 fontCoord = asciiCoord * uint2(COLOR_PICKER_FONT_WIDTH, COLOR_PICKER_FONT_HEIGHT) + localCoord;
        float2 fontUv = float2(fontCoord) / float2(COLOR_PICKER_FONT_WIDTH * COLOR_PICKER_FONT_COUNT_X, COLOR_PICKER_FONT_HEIGHT * COLOR_PICKER_FONT_COUNT_Y);

#if UNITY_UV_STARTS_AT_TOP
        fontUv.y = 1.0 - fontUv.y;
#endif

        float glyph = SAMPLE_TEXTURE2D_LOD(_ColorPickerDebugFont, sampler_PointClamp, fontUv, 0).r;
        color = lerp(color, _ColorPickerFontColor, glyph);
    }

    textCoord.x += GetColorPickerFontAdvance() * direction;
}

void DrawColorPickerCharacter(uint asciiValue, int2 currentCoord, inout int2 textCoord, inout float3 color)
{
    DrawColorPickerCharacter(asciiValue, currentCoord, textCoord, color, 1);
}

void DrawColorPickerInteger(int value, int2 currentCoord, inout int2 textCoord, inout float3 color, int leadingZeroCount, bool forceNegativeSign)
{
    const uint maxCharacterCount = 16;
    uint absoluteValue = abs(value);
    int characterCount = min((value == 0 ? 0 : log10(absoluteValue)) + ((value < 0 || forceNegativeSign) ? 1 : 0) + leadingZeroCount, maxCharacterCount);
    int fontAdvance = GetColorPickerFontAdvance();
    textCoord.x += characterCount * fontAdvance;

    bool drawCharacter = true;
    for (uint index = 0; index < maxCharacterCount; ++index)
    {
        if (drawCharacter)
            DrawColorPickerCharacter((absoluteValue % 10) + '0', currentCoord, textCoord, color, -1);

        if (absoluteValue < 10)
            drawCharacter = false;

        absoluteValue /= 10;
    }

    for (int index = 0; index < leadingZeroCount; ++index)
        DrawColorPickerCharacter('0', currentCoord, textCoord, color, -1);

    if (value < 0 || forceNegativeSign)
        DrawColorPickerCharacter('-', currentCoord, textCoord, color, -1);

    textCoord.x += (characterCount + 2) * fontAdvance;
}

void DrawColorPickerInteger(int value, int2 currentCoord, inout int2 textCoord, inout float3 color)
{
    DrawColorPickerInteger(value, currentCoord, textCoord, color, 0, false);
}

void DrawColorPickerFloat(float value, int2 currentCoord, inout int2 textCoord, inout float3 color)
{
    if (value != value)
    {
        DrawColorPickerCharacter('N', currentCoord, textCoord, color);
        DrawColorPickerCharacter('a', currentCoord, textCoord, color);
        DrawColorPickerCharacter('N', currentCoord, textCoord, color);
        return;
    }

    const uint digitCount = 6;
    int integerValue = int(value);
    DrawColorPickerInteger(integerValue, currentCoord, textCoord, color, 0, value < 0.0);
    DrawColorPickerCharacter('.', currentCoord, textCoord, color);
    int fractionValue = int(frac(abs(value)) * pow(10, digitCount));
    int leadingZeroCount = digitCount - (int(log10(max(fractionValue, 1))) + 1);
    DrawColorPickerInteger(fractionValue, currentCoord, textCoord, color, leadingZeroCount, false);
}

void DrawColorPickerValue(uint label, float value, bool displayAsByte, int2 currentCoord, inout int2 textCoord, inout float3 color)
{
    DrawColorPickerCharacter(label, currentCoord, textCoord, color);
    DrawColorPickerCharacter(':', currentCoord, textCoord, color);

    if (displayAsByte)
        DrawColorPickerInteger(int(value * 255.5), currentCoord, textCoord, color);
    else
        DrawColorPickerFloat(value, currentCoord, textCoord, color);
}

float2 GetColorPickerSourceUv(float2 outputUv)
{
    return outputUv;
}

bool IsColorPickerMouseValid()
{
    return _ColorPickerMode != COLOR_PICKER_MODE_NONE &&
        all(_ColorPickerMousePixelCoord.zw >= 0.0) &&
        all(_ColorPickerMousePixelCoord.zw <= 1.0);
}

float4 SampleColorPickerColor()
{
    float2 sourceUv = GetColorPickerSourceUv(_ColorPickerMousePixelCoord.zw);
    return SAMPLE_TEXTURE2D_X_LOD(_ColorPickerDebugTexture, sampler_PointClamp, sourceUv, 0);
}

float4 ApplyColorPicker(Varyings input, float4 displayedColor, float4 pickedColor)
{
    if (!IsColorPickerMouseValid())
        return displayedColor;

    bool displayAsByte = _ColorPickerMode == COLOR_PICKER_MODE_BYTE || _ColorPickerMode == COLOR_PICKER_MODE_BYTE4;
    bool displayFourChannels = _ColorPickerMode == COLOR_PICKER_MODE_BYTE4 || _ColorPickerMode == COLOR_PICKER_MODE_FLOAT4;
    float fontScale = GetColorPickerFontScale();
    int lineHeight = GetColorPickerLineHeight();
    int2 currentCoord = int2(input.positionCS.xy - _ColorPickerScreenSize.zw);
    if (ShouldFlipColorPickerCoordinates())
        currentCoord.y = (int)_ColorPickerScreenSize.y - 1 - currentCoord.y;

    int textPositionX = (int)(_ColorPickerMousePixelCoord.x + 1.5 * COLOR_PICKER_FONT_WIDTH * fontScale);
    int textPositionY = (int)_ColorPickerMousePixelCoord.y + lineHeight * (displayFourChannels ? 4 : 1);
    int2 textCoord = int2(textPositionX, textPositionY);

    DrawColorPickerValue(displayAsByte ? 'R' : 'X', pickedColor.x, displayAsByte, currentCoord, textCoord, displayedColor.rgb);
    if (displayFourChannels)
    {
        textCoord.x = textPositionX;
        textCoord.y -= lineHeight;
        DrawColorPickerValue(displayAsByte ? 'G' : 'Y', pickedColor.y, displayAsByte, currentCoord, textCoord, displayedColor.rgb);
        textCoord.x = textPositionX;
        textCoord.y -= lineHeight;
        DrawColorPickerValue(displayAsByte ? 'B' : 'Z', pickedColor.z, displayAsByte, currentCoord, textCoord, displayedColor.rgb);
        textCoord.x = textPositionX;
        textCoord.y -= lineHeight;
        DrawColorPickerValue(displayAsByte ? 'A' : 'W', pickedColor.w, displayAsByte, currentCoord, textCoord, displayedColor.rgb);
    }

    return displayedColor;
}

#endif
