// The shading pass of RenderTextureFoldRenderer (Bible §4): every pixel of the sheet's visible footprint is redrawn
// from a copy of the screen taken after the Default sorting layer (the 2D renderer's Camera Sorting Layer Texture),
// sampled a little way off - drawn in toward each crease by a smooth pinch, and moved by the grain's broad wrinkles -
// and lit by the paper's grain and creases from a fixed Desk-space light. So everything on the sheet - the faces'
// drawings, the player, blocks, universal walls - bends with its creases and wrinkles and takes their light (Aaron, 2026-10-07/08: "They live on the paper too"; not "a
// dirty window"). One part per layer of the folded sheet, sharing that layer's mesh and face uvs; parts draw
// topmost layer first and a stencil bit lets only the topmost part redraw a pixel. The offset never reaches past
// the part's outline (its layer's edges and those of every layer above), so nothing is pulled across a Flap's
// edge, a Seam or the sheet's edge. Every property is set per part by the renderer's property block; the warp
// maths mirrors SheetWarp and the crease maths CreaseShading, line for line.
Shader "Papercut/Sheet Shading"
{
    Properties
    {
        [Normal] _GrainMap ("Grain (normal map)", 2D) = "bump" {}
        _GrainStrength ("Grain strength (0 = flat)", Range(0, 4)) = 0
        _GrainAmbient ("Grain ambient (1 = unshaded)", Range(0, 1)) = 0.6
        _LightDir ("Light direction (Desk space)", Vector) = (-0.5, 0.6, 1, 0)
        [HideInInspector] _GrainFrame ("Grain frame (face to Desk, columns)", Vector) = (1, 0, 0, 1)
        [HideInInspector] _SheetSize ("Sheet size (sheet units)", Vector) = (11, 8.5, 0, 0)

        [Normal] _CreaseInsideMap ("Crease, inside of the fold (normal map)", 2D) = "bump" {}
        [Normal] _CreaseOutsideMap ("Crease, outside of the fold (normal map)", 2D) = "bump" {}
        [HideInInspector] _CreaseCount ("Crease count", Float) = 0
        [HideInInspector] _CreaseLineV ("Crease line height in the image (x inside, y outside)", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _CreaseFlip ("Crease image turned half round (x inside, y outside: +1 / -1)", Vector) = (1, 1, 0, 0)
        [HideInInspector] _CreaseMapWidth ("Crease image width (sheet units)", Float) = 11
        [HideInInspector] _CreaseHalfWidth ("Crease band half width (sheet units)", Float) = 0.5
        [HideInInspector] _CreaseFadeStart ("Crease fade start (fraction of the band)", Float) = 0.5
        [HideInInspector] _CreaseStrength ("Live crease strength", Float) = 1
        [HideInInspector] _RememberedCreaseStrength ("Remembered crease strength", Float) = 0.4

        [HideInInspector] _GrainWarp ("Grain warp (sheet units per unit of broad slope)", Float) = 0
        [HideInInspector] _GrainWarpMip ("Grain warp blur (mip level)", Float) = 4
        [HideInInspector] _CreaseInsidePinch ("Inside crease pinch", Float) = 0
        [HideInInspector] _CreaseOutsidePinch ("Outside crease pinch", Float) = 0
        [HideInInspector] _RememberedCreasePinch ("Remembered crease pinch multiplier", Float) = 0.4
        [HideInInspector] _OutlineCount ("Outline segment count (-1 = no warp)", Float) = -1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        // Replace: the pixel becomes the warped copy, lit.
        Blend One Zero
        ZWrite Off
        Cull Off

        // The topmost layer's part draws first and claims the pixel; lower layers' parts skip it.
        Stencil
        {
            Ref 128
            ReadMask 128
            WriteMask 128
            Comp NotEqual
            Pass Replace
        }

        Pass
        {
            Name "Sheet Shading"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            // CreaseShading.MaxCreases: the length of the crease arrays (a test reads this line and compares).
            #define MAX_CREASES 64
            // SheetWarp.MaxOutlineSegments: the length of the outline array (a test reads this line and compares).
            #define MAX_OUTLINE 64

            TEXTURE2D(_GrainMap);
            SAMPLER(sampler_GrainMap);
            TEXTURE2D(_CreaseInsideMap);
            TEXTURE2D(_CreaseOutsideMap);
            // The screen as drawn up to the end of the Default sorting layer (Renderer2D: Camera Sorting Layer Texture).
            TEXTURE2D(_CameraSortingLayerTexture);
            // The crease maps and the screen copy sample through URP's global sampler_LinearClamp (GlobalSamplers.hlsl).

            CBUFFER_START(UnityPerMaterial)
                half _GrainStrength;
                half _GrainAmbient;
                float4 _LightDir;
                float4 _GrainFrame;
                float4 _SheetSize;
                float _CreaseCount;
                float4 _CreaseLineV;
                float4 _CreaseFlip;
                float _CreaseMapWidth;
                float _CreaseHalfWidth;
                float _CreaseFadeStart;
                half _CreaseStrength;
                half _RememberedCreaseStrength;
                float _GrainWarp;
                float _GrainWarpMip;
                float _CreaseInsidePinch;
                float _CreaseOutsidePinch;
                float _RememberedCreasePinch;
                float _OutlineCount;
            CBUFFER_END

            // Per face, set by the renderer's property block (arrays cannot be material properties). Each entry is a
            // crease in the face's authored space: (midpoint.xy, unit normal toward the side that lifted) and
            // (half length, map: 0 inside / 1 outside, remembered: 0 / 1, open ends: 1 = the -t end, 2 = the +t end,
            // each on the sheet border and so not cut). See CreaseShading.Pack.
            float4 _CreaseLines[MAX_CREASES];
            float4 _CreaseInfo[MAX_CREASES];
            // Per part: the edges its offset must stay inside, sheet-local (a.xy, b.xy). See SheetWarp.PackOutline.
            float4 _Outline[MAX_OUTLINE];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 faceUV : TEXCOORD0;
                float2 sheetPos : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.faceUV = input.uv;
                output.sheetPos = input.positionOS.xy; // the mesh is sheet-local
                return output;
            }

            // SheetWarp.EndTaper: a crease's warp eases to 0 over the last half width before a closed end.
            float EndTaper(float unflippedAlong, float halfLength, int flags)
            {
                float taper = 1.0;
                if ((flags & 1) == 0)
                    taper = min(taper, saturate((halfLength + unflippedAlong) / _CreaseHalfWidth));
                if ((flags & 2) == 0)
                    taper = min(taper, saturate((halfLength - unflippedAlong) / _CreaseHalfWidth));
                return taper;
            }

            // The creases at face point p, in face space: shade is the strongest crease's normal offset (as lit;
            // the strongest wins, so a repeated crease never doubles), warp the creases' pinches (where to sample, so
            // what is drawn is drawn in toward the crease) combined as SheetWarp.CombineWarps (sum |v|^2 v / sum |v|^2:
            // continuous where creases cross). Mirrors
            // CreaseShading.MapPoint and CreaseShading.Fade: the image is laid along the crease centred on its
            // midpoint, its crease line on the crease, its top toward the side that lifted (turned half round when
            // flipped), and it repeats mirrored past its ends.
            void Creases(float2 p, out half2 shade, out float2 warp)
            {
                shade = 0;
                half bestSq = 0;
                float2 warpSum = 0;
                float weightSum = 0;
                int count = (int)_CreaseCount;
                float mapWidth = _CreaseMapWidth;
                float mapHeight = mapWidth * _SheetSize.y / _SheetSize.x; // the image is a picture of a whole sheet

                [loop]
                for (int i = 0; i < MAX_CREASES; i++)
                {
                    if (i >= count)
                        break;
                    float4 seg = _CreaseLines[i];
                    float4 info = _CreaseInfo[i];
                    bool outside = info.y > 0.5;
                    bool remembered = info.z > 0.5;

                    float flip = outside ? _CreaseFlip.y : _CreaseFlip.x;
                    float2 n = seg.zw * flip;
                    float2 t = float2(n.y, -n.x);
                    float2 rel = p - seg.xy;
                    float along = dot(rel, t);
                    float across = dot(rel, n);
                    float unflippedAlong = along * flip;
                    int flags = (int)round(info.w);
                    if ((unflippedAlong < -info.x && (flags & 1) == 0) || (unflippedAlong > info.x && (flags & 2) == 0)
                        || abs(across) > _CreaseHalfWidth)
                        continue;

                    float x = 0.5 + along / mapWidth;
                    float m = fmod(abs(x), 2.0);
                    bool forward = m <= 1.0;
                    float alongSign = (forward ? 1.0 : -1.0) * (x >= 0.0 ? 1.0 : -1.0);
                    float2 uv = float2(forward ? m : 2.0 - m, (outside ? _CreaseLineV.y : _CreaseLineV.x) + across / mapHeight);

                    half4 packed;
                    if (outside)
                        packed = SAMPLE_TEXTURE2D_LOD(_CreaseOutsideMap, sampler_LinearClamp, uv, 0);
                    else
                        packed = SAMPLE_TEXTURE2D_LOD(_CreaseInsideMap, sampler_LinearClamp, uv, 0);
                    half2 slope = UnpackNormal(packed).xy;
                    slope.x *= alongSign;
                    half fade = 1.0h - smoothstep(_CreaseFadeStart, 1.0, abs(across) / _CreaseHalfWidth);

                    half2 c = slope * fade * (remembered ? _RememberedCreaseStrength : _CreaseStrength);
                    half sq = dot(c, c);
                    if (sq > bestSq)
                    {
                        bestSq = sq;
                        shade = half2(t * c.x + n * c.y);
                    }

                    // SheetWarp.CreaseWarp: a smooth pinch across the band, independent of the map's pixels (the map
                    // only lights). SheetWarp.Pinch: sign(a) * w * x * (1 - x)^2, x = |a| / w.
                    float pinchScale = (outside ? _CreaseOutsidePinch : _CreaseInsidePinch) * (remembered ? _RememberedCreasePinch : 1.0);
                    float x01 = saturate(abs(across) / _CreaseHalfWidth);
                    float pinch = sign(across) * _CreaseHalfWidth * x01 * (1.0 - x01) * (1.0 - x01);
                    float2 v = n * (pinch * EndTaper(unflippedAlong, info.x, flags) * pinchScale);
                    float w = dot(v, v);
                    warpSum += v * w;
                    weightSum += w;
                }
                warp = weightSum > 1e-12 ? warpSum / weightSum : float2(0, 0);
            }

            // SheetWarp.DistanceToOutline: 0 when the part must not warp (_OutlineCount < 0).
            float OutlineDistance(float2 p)
            {
                int count = (int)round(_OutlineCount);
                if (count < 0)
                    return 0.0;
                float best = 1e6;
                [loop]
                for (int i = 0; i < MAX_OUTLINE; i++)
                {
                    if (i >= count)
                        break;
                    float4 s = _Outline[i];
                    float2 ab = s.zw - s.xy;
                    float t = saturate(dot(p - s.xy, ab) / max(dot(ab, ab), 1e-12));
                    best = min(best, length(p - (s.xy + ab * t)));
                }
                return best;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Screen pixels per sheet unit and back: the sheet maps to the screen affinely, so the derivatives
                // of the sheet position are exact and carry an offset in sheet units to an offset in pixels with no
                // assumption about the target's orientation.
                float2 dx = ddx(input.sheetPos);
                float2 dy = ddy(input.sheetPos);
                float margin = length(dx) + length(dy); // a pixel's diagonal: a bilinear sample's reach at any edge angle

                // The grain and the creases, in face space (the grain shares the face's texture coordinates, so each
                // texel stays on its point of the paper); _GrainFrame (the linear part of the piece's pose) carries
                // them into Desk (sheet-local) space, where the light is fixed.
                half3 grain = UnpackNormalScale(SAMPLE_TEXTURE2D(_GrainMap, sampler_GrainMap, input.faceUV), _GrainStrength);
                // For the warp, only the grain's broad wrinkles (a blurred mip, so no pixel-level smear), less the
                // map's average lean (its smallest mip), so a map that leans does not shift the whole sheet.
                float2 grainSlope = UnpackNormal(SAMPLE_TEXTURE2D_LOD(_GrainMap, sampler_GrainMap, input.faceUV, _GrainWarpMip)).xy
                    - UnpackNormal(SAMPLE_TEXTURE2D_LOD(_GrainMap, sampler_GrainMap, input.faceUV, 16)).xy;
                float2 facePoint = (input.faceUV - 0.5) * _SheetSize.xy;
                half2 creaseShade;
                float2 creaseWarp;
                Creases(facePoint, creaseShade, creaseWarp);

                half3 normalFace = normalize(half3(grain.xy + creaseShade, grain.z));
                half2 normalDeskXY = _GrainFrame.xy * normalFace.x + _GrainFrame.zw * normalFace.y;
                half3 normalDesk = normalize(half3(normalDeskXY, normalFace.z));
                half3 light = normalize(_LightDir.xyz);

                // Normalised so that flat paper keeps its colour exactly; bumps go lighter or darker around it.
                half lit = _GrainAmbient + (1.0h - _GrainAmbient) * saturate(dot(normalDesk, light));
                half flat = _GrainAmbient + (1.0h - _GrainAmbient) * saturate(light.z);
                half ratio = clamp(lit / max(flat, 1e-3h), 0.0h, 2.0h);

                // The warp (SheetWarp.Offset): where to sample, so what is drawn is drawn in toward creases and slides
                // down the broad wrinkles; held short of the part's outline (SheetWarp.FadedOffset).
                float2 warpFace = creaseWarp - _GrainWarp * grainSlope;
                float2 offset = _GrainFrame.xy * warpFace.x + _GrainFrame.zw * warpFace.y;
                float maxLength = max(OutlineDistance(input.sheetPos) - margin, 0.0);
                float offsetLength = length(offset);
                if (offsetLength > maxLength)
                    offset *= maxLength / max(offsetLength, 1e-6);

                // Sheet units to pixels: solve [dx dy] * pixels = offset.
                float det = dx.x * dy.y - dy.x * dx.y;
                float2 pixels = abs(det) > 1e-12
                    ? float2(offset.x * dy.y - dy.x * offset.y, dx.x * offset.y - offset.x * dx.y) / det
                    : float2(0, 0);
                // The copy has the orientation of the target being drawn, so a pixel position maps straight to its
                // texel. (Not GetNormalizedScreenSpaceUV: its y flip reads _ScaleBiasRt, which the 2D renderer's draw
                // pass leaves unset, collapsing every pixel onto one row.)
                float2 screenUV = (input.positionCS.xy + pixels) / _ScaledScreenParams.xy;
                half4 drawn = SAMPLE_TEXTURE2D_LOD(_CameraSortingLayerTexture, sampler_LinearClamp, screenUV, 0);
                return half4(drawn.rgb * ratio, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
