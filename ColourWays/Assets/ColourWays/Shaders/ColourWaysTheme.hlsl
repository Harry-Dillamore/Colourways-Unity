// Light / dark theme shared by every ColourWays shader.
// _CW_Dark (global, 0..1, animated by ColourWays.Theme): 0 = pure white world, 1 = black world.
// In dark mode neutrals invert (white -> black, ink -> white, grey dither dots -> light dots on
// black) while saturated colours are pushed brighter and more saturated: neon paint on black.
#ifndef CW_THEME_INCLUDED
#define CW_THEME_INCLUDED

float _CW_Dark;

half3 CWNeon(half3 c)
{
    half lum = dot(c, half3(0.2126, 0.7152, 0.0722));
    return saturate((lum + (c - lum) * 1.3) * 1.15);
}

half3 CWTheme(half3 c)
{
    half mx = max(c.r, max(c.g, c.b));
    half mn = min(c.r, min(c.g, c.b));
    half neutral = 1.0 - saturate((mx - mn) * 4.0);
    half3 dark = lerp(CWNeon(c), 1.0 - c, neutral);
    return lerp(c, dark, saturate(_CW_Dark));
}

#endif
