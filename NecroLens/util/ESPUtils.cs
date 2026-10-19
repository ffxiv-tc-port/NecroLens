using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using NecroLens.Data;
using NecroLens.Model;
using NecroLens.Service;

namespace NecroLens.util;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class ESPUtils
{
    public const float DefaultCircleThickness = 2f;
    public const float DefaultFilledOpacity = 0.4f;
    public const int CircleSegments = 50;
    public const float CircleSegmentFullRotation = 2 * MathF.PI / CircleSegments;

    public static float Distance2D(this Vector3 v, Vector3 v2)
    {
        return new Vector2(v.X - v2.X, v.Z - v2.Z).Length();
    }

    public static bool IsIgnoredObject(IGameObject gameObject)
    {
        if (DataIds.IgnoredDataIDs.Contains(gameObject.BaseId)) return true;
        if (gameObject.IsDead || gameObject is IBattleNpc { CurrentHp: <= 0 }) return true;

        return false;
    }

    /// <summary>
    /// 把弱點 bitflags 轉成標籤上的縮寫。字取自台服官方狀態名：
    /// 眩＝眩暈、重＝加重、眠＝睡眠、縛＝束縛、減＝減速。
    /// </summary>
    /// <remarks>
    /// 負值＝MonsterDex 沒有這隻怪的資料（或這一幀還沒查到）⇒ 回「?」。
    /// 🔴 不回空字串：「不知道」本身必須在列上看得見，畫成留白會被讀成「這隻怪沒有弱點」。
    /// 0＝查到了、而且五種狀態都不吃 ⇒ 回「-」，那是確定的資訊，與「?」不是同一件事。
    /// </remarks>
    public static string VulnerabilityText(int flags)
    {
        if (flags < 0)
            return "?";

        var text = "";
        if ((flags & MobVulnerabilityService.BitStun) != 0) text += Strings.ESP_Vulnerability_Stun;
        if ((flags & MobVulnerabilityService.BitHeavy) != 0) text += Strings.ESP_Vulnerability_Heavy;
        if ((flags & MobVulnerabilityService.BitSleep) != 0) text += Strings.ESP_Vulnerability_Sleep;
        if ((flags & MobVulnerabilityService.BitBind) != 0) text += Strings.ESP_Vulnerability_Bind;
        if ((flags & MobVulnerabilityService.BitSlow) != 0) text += Strings.ESP_Vulnerability_Slow;

        return text.Length == 0 ? "-" : text;
    }

    /// <summary>把 <see cref="VulnerabilityText"/> 的結果接到 <paramref name="label"/> 尾端（不配置新字串）。</summary>
    public static void AppendVulnerabilityText(LabelBuffer label, int flags)
    {
        if (flags < 0)
        {
            label.Append('?');
            return;
        }

        var start = label.Length;
        if ((flags & MobVulnerabilityService.BitStun) != 0) label.Append(Strings.ESP_Vulnerability_Stun);
        if ((flags & MobVulnerabilityService.BitHeavy) != 0) label.Append(Strings.ESP_Vulnerability_Heavy);
        if ((flags & MobVulnerabilityService.BitSleep) != 0) label.Append(Strings.ESP_Vulnerability_Sleep);
        if ((flags & MobVulnerabilityService.BitBind) != 0) label.Append(Strings.ESP_Vulnerability_Bind);
        if ((flags & MobVulnerabilityService.BitSlow) != 0) label.Append(Strings.ESP_Vulnerability_Slow);

        if (label.Length == start) label.Append('-');
    }

    // 繪製執行緒專用：DrawName 每次呼叫都先 Clear 再組。
    private static readonly LabelBuffer NameLabel = new();
    private static readonly LabelBuffer VulnerabilityLabel = new();

    public static void DrawName(ImDrawListPtr drawList, ESPObject espObject, Vector2 position)
    {
        var label = NameLabel;
        label.Clear();
        espObject.AppendName(label);

        if (espObject.Type == ESPObject.ESPType.GoldChest && espObject.ContainingPomander != null)
        {
            label.Append('\n');
            label.Append(DungeonService.PomanderNames[espObject.ContainingPomander.Value]);
        }

        // 弱點縮寫必須另外呼叫一次 AddText：它的顏色與名稱不同（未知是灰色的「?」），
        // 而 AddText 一次只吃一種顏色，串進 name 裡就沒辦法分色了。
        // 這裡只讀快取，不呼叫 IPC——補快取是 ESPService 在 framework 執行緒上做的。
        var vulnerability = VulnerabilityLabel;
        vulnerability.Clear();
        var hasVulnerability = false;
        var vulnerabilityColor = 0u;
        if (Config.ShowMobVulnerabilities && label.Length > 0 && espObject.NameId != 0 &&
            espObject.Type is ESPObject.ESPType.Enemy or ESPObject.ESPType.Mimic &&
            VulnerabilityService.IsAvailable)
        {
            var flags = VulnerabilityService.GetFlags(espObject.NameId);
            vulnerability.Append(' ');
            AppendVulnerabilityText(vulnerability, flags);
            hasVulnerability = true;
            vulnerabilityColor = flags < 0 ? Color.Gray.ToUint() : Color.Aquamarine.ToUint();
        }

        var name = label.Span;
        var textSize = ImGui.CalcTextSize(name);
        var vulnerabilitySize = hasVulnerability ? ImGui.CalcTextSize(vulnerability.Span) : Vector2.Zero;
        // Center name on position
        var textPosition = new Vector2(position.X - ((textSize.X + vulnerabilitySize.X) / 2f),
                                       position.Y + (textSize.Y / 2f));
        drawList.AddText(textPosition, espObject.RenderColor(), name);

        if (hasVulnerability)
        {
            // 名稱可能是多行（金寶箱的魔石、除錯資訊），縮寫要接在第一行的尾巴，
            // 不能拿整塊的寬度去推位置。
            var firstLine = name;
            var lineBreak = name.IndexOf('\n');
            if (lineBreak >= 0) firstLine = name[..lineBreak];

            drawList.AddText(textPosition with { X = textPosition.X + ImGui.CalcTextSize(firstLine).X },
                             vulnerabilityColor, vulnerability.Span);
        }
    }

    public static void DrawPlayerDot(ImDrawListPtr drawList, Vector2 position)
    {
        drawList.AddCircleFilled(position, 3f, Config.PlayerDotColor, 100);
    }

    public static void DrawInteractionCircle(ImDrawListPtr drawList, ESPObject espObject, float radius)
    {
        var color = Color.White.ToUint(1 - (espObject.Distance() / (radius + 5)));
        DrawCircleInternal(drawList, espObject, radius, color, false, 1f);
        drawList.PathClear();
    }

    public static void DrawConeFromCenterPoint(
        ImDrawListPtr drawList, ESPObject espObject, float angleRadian, float radius, uint outlineColor)
    {
        var position = espObject.Position;
        var rotation = espObject.Rotation + (MathF.PI / 4);
        var partialCircleSegmentRotation = angleRadian / CircleSegments;
        var coneColor = outlineColor.SetAlpha(0.2f);

        GameGui.WorldToScreen(new Vector3(position.X, position.Y, position.Z),
                                            out var originPositionOnScreen);
        drawList.PathLineTo(originPositionOnScreen);
        for (var i = 0; i <= CircleSegments; i++)
        {
            var currentRotation = rotation - (i * partialCircleSegmentRotation);
            var xValue = radius * MathF.Sin(currentRotation);
            var yValue = radius * MathF.Cos(currentRotation);
            GameGui.WorldToScreen(new Vector3(position.X + xValue, position.Y, position.Z + yValue),
                                                out var segmentVectorOnCircle);
            drawList.PathLineTo(segmentVectorOnCircle);
        }

        drawList.PathFillConvex(coneColor);
        drawList.PathClear();
        drawList.PathLineTo(originPositionOnScreen);
        for (var i = 0; i <= CircleSegments; i++)
        {
            var currentRotation = rotation - (i * partialCircleSegmentRotation);
            var xValue = radius * MathF.Sin(currentRotation);
            var yValue = radius * MathF.Cos(currentRotation);
            GameGui.WorldToScreen(new Vector3(position.X + xValue, position.Y, position.Z + yValue),
                                                out var segmentVectorOnCircle);
            drawList.PathLineTo(segmentVectorOnCircle);
        }

        drawList.PathLineTo(originPositionOnScreen);

        drawList.PathStroke(outlineColor);
    }

    public static void DrawCircleFilled(
        ImDrawListPtr drawList, ESPObject espObject, float radius, uint circleColor,
        float thickness = DefaultCircleThickness)
    {
        var filledColor = circleColor.SetAlpha(0.15f);

        DrawCircleInternal(drawList, espObject, radius, circleColor, false, thickness);
        DrawCircleInternal(drawList, espObject, radius, filledColor, true, thickness);
        drawList.PathClear();
    }

    public static void DrawCircle(
        ImDrawListPtr drawList, ESPObject espObject, float radius, uint color,
        float opacity = 1f, float thickness = DefaultCircleThickness)
    {
        DrawCircleInternal(drawList, espObject, radius, color.SetAlpha(opacity), false, thickness);
        drawList.PathClear();
    }

    private static void DrawDottedCircle(
        ImDrawListPtr drawList, ESPObject espObject, float radius, uint color, float thickness)
    {
        var position = espObject.Position;
        var circleSegmentFullRotation = 2 * MathF.PI / 60;
        for (var i = 0; i <= 60; i++)
        {
            var currentRotation = i * circleSegmentFullRotation;
            var xValue = radius * MathF.Sin(currentRotation);
            var yValue = radius * MathF.Cos(currentRotation);
            GameGui.WorldToScreen(new Vector3(position.X + xValue, position.Y, position.Z + yValue),
                                                out var segment);
            // drawList.PathLineTo(segment);
            drawList.PathArcTo(segment, 1f, 1f, 1f);
            drawList.PathStroke(color, ImDrawFlags.RoundCornersDefault, thickness);
        }
    }

    private static readonly (float, float)[] FacingArrowPoints =
    [
        (4, -40), (6f, 0), (4, 40), (3.9f, 11), (2f, 22), (2f, -22), (3.9f, -11), (4, -40)
    ];

    public static void DrawFacingDirectionArrow(
        ImDrawListPtr drawList, ESPObject espObject, uint color,
        float opacity = 1f, float thickness = DefaultCircleThickness)
    {
        foreach (var (radian, steps) in FacingArrowPoints)
            drawList.PathLineTo(CreatePointAroundObjectOnScreen(espObject, radian, steps));

        drawList.PathStroke(color.SetAlpha(opacity), ImDrawFlags.RoundCornersDefault, thickness);
        drawList.PathClear();
    }

    private static void DrawCircleInternal(
        ImDrawListPtr drawList, ESPObject espObject, float radius, uint color, bool filled,
        float thickness)
    {
        var position = espObject.Position;
        for (var i = 0; i <= CircleSegments; i++)
        {
            var currentRotation = i * CircleSegmentFullRotation;
            var xValue = radius * MathF.Sin(currentRotation);
            var yValue = radius * MathF.Cos(currentRotation);
            GameGui.WorldToScreen(new Vector3(position.X + xValue, position.Y, position.Z + yValue),
                                                out var segment);
            drawList.PathLineTo(segment);
        }

        if (filled)
            drawList.PathFillConvex(color);
        else
            drawList.PathStroke(color, ImDrawFlags.RoundCornersDefault, thickness);
    }

    private static Vector2 CreatePointAroundObjectOnScreen(ESPObject espObject, float radian, float steps)
    {
        var pos = espObject.Position;
        var rotation = espObject.Rotation;
        var partialCircleSegmentRotation = 2 * MathF.PI / 1000;
        var stepRotation = rotation - (steps * partialCircleSegmentRotation);
        var xValue = radian * MathF.Sin(stepRotation);
        var yValue = radian * MathF.Cos(stepRotation);
        var stepPos = pos with { X = pos.X + xValue, Z = pos.Z + yValue };
        GameGui.WorldToScreen(stepPos, out var segment);
        return segment;
    }
}
