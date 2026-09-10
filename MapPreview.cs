using System;
using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace MapPreview
{
    [ModInitializer(nameof(Initialize))]
    public static class Plugin
    {
        public static void Initialize()
        {
            new Harmony("com.shina.sts2.mappreview").PatchAll(typeof(Plugin).Assembly);
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    internal static class MapOpenPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NMapScreen __instance)
        {
            if (__instance.GetNodeOrNull(new NodePath(MapPreviewOverlay.NodeName)) == null)
            {
                var overlay = new MapPreviewOverlay(__instance) { Name = MapPreviewOverlay.NodeName };
                __instance.AddChild(overlay);
            }
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Close))]
    internal static class MapClosePatch
    {
        [HarmonyPrefix]
        private static void Prefix(NMapScreen __instance)
        {
            var overlay = __instance.GetNodeOrNull(new NodePath(MapPreviewOverlay.NodeName)) as MapPreviewOverlay;
            if (overlay != null)
            {
                overlay.RestoreMap();
                overlay.QueueFree();
            }
        }
    }

    [HarmonyPatch(typeof(NMapScreen), "UpdateScrollPosition")]
    internal static class DisableMapScrollPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NMapScreen __instance)
        {
            return !MapPreviewOverlay.IsPreviewing(__instance);
        }
    }

    [HarmonyPatch(typeof(NMapDrawingInput), nameof(NMapDrawingInput._Ready))]
    internal static class PreviewDrawingInputPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NMapDrawingInput __instance)
        {
            var screen = __instance.GetParent() as NMapScreen;
            MapPreviewOverlay.Find(screen)?.PrepareDrawingInput(__instance);
        }
    }

    [HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.BeginLineLocal))]
    internal static class PreviewDrawingStartPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NMapDrawings __instance, ref Vector2 __0)
        {
            MapPreviewOverlay.FindFor(__instance)?.ReplaceWithPreviewPointer(__instance, ref __0);
        }
    }

    [HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.UpdateCurrentLinePositionLocal))]
    internal static class PreviewDrawingPositionPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NMapDrawings __instance, ref Vector2 __0)
        {
            MapPreviewOverlay.FindFor(__instance)?.ReplaceWithPreviewPointer(__instance, ref __0);
        }
    }

    [HarmonyPatch(typeof(NMapDrawings), "BeginLine")]
    internal static class PreviewDrawingLineStartPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NMapDrawings __instance, ref Vector2 __1)
        {
            MapPreviewOverlay.FindFor(__instance)?.ReplaceWithPreviewPointer(__instance, ref __1);
        }
    }

    [HarmonyPatch(typeof(NMapDrawings), "UpdateCurrentLinePosition")]
    internal static class PreviewDrawingLineUpdatePatch
    {
        [HarmonyPrefix]
        private static void Prefix(NMapDrawings __instance, ref Vector2 __1)
        {
            MapPreviewOverlay.FindFor(__instance)?.ReplaceWithPreviewPointer(__instance, ref __1);
        }
    }

    [HarmonyPatch(typeof(NMapDrawings), "ToNetPosition")]
    internal static class PreviewDrawingNetworkPositionPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NMapDrawings __instance, ref Vector2 __0)
        {
            MapPreviewOverlay.FindFor(__instance)?.ReplaceWithPreviewPointer(__instance, ref __0);
        }
    }

    [HarmonyPatch(typeof(NMapScreen), "_Process")]
    internal static class KeepPreviewTransformPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NMapScreen __instance)
        {
            var overlay = __instance.GetNodeOrNull(new NodePath(MapPreviewOverlay.NodeName)) as MapPreviewOverlay;
            if (overlay != null)
            {
                overlay.KeepPreviewFitted();
                overlay.RememberNormalMapPosition();
            }
        }
    }

    internal sealed class MapPreviewOverlay : CanvasLayer
    {
        internal const string NodeName = "MapPreviewOverlay";

        private static readonly FieldInfo[] LayerFields =
        {
            Field("_mapContainer"), Field("_pathsContainer"), Field("_points"), Field("_mapBgContainer"), Field("_marker")
        };
        private static readonly FieldInfo MapPointDictionaryField = Field("_mapPointDictionary");
        private static readonly FieldInfo StartingPointField = Field("_startingPointNode");
        private static readonly FieldInfo BossPointField = Field("_bossPointNode");
        private static readonly FieldInfo SecondBossPointField = Field("_secondBossPointNode");
        private static readonly PropertyInfo DrawingsProperty = AccessTools.Property(typeof(NMapScreen), "Drawings");
        private static bool _hasSavedButtonPosition;
        private static Vector2 _savedButtonPosition;

        private readonly NMapScreen _screen;
        private readonly Button _button;
        private LayerState[] _layers;
        private Vector2 _previewOrigin;
        private Vector2 _previewSize;
        private float _previewZoom;
        private bool _clipContents;
        private bool _isPreviewing;
        private bool _isRightDragging;

        internal bool PreviewActive => _isPreviewing;

        internal MapPreviewOverlay(NMapScreen screen)
        {
            _screen = screen;
            Layer = 100;
            _button = CreateButton();
            AddChild(_button);
        }

        private Button CreateButton()
        {
            Rect2 viewport = _screen.GetViewport().GetVisibleRect();
            var button = new Button
            {
                Name = "ViewMapButton",
                Text = "查看地图",
                TooltipText = "显示完整地图缩略图；再次点击恢复。右键拖动可调整位置。",
                Position = _hasSavedButtonPosition ? _savedButtonPosition : new Vector2(viewport.Size.X - 315f, viewport.Size.Y - 140f),
                Size = new Vector2(265f, 60f),
                MouseFilter = Control.MouseFilterEnum.Stop
            };

            button.AddThemeStyleboxOverride("normal", MakeStyle(new Color(.18f, .50f, .52f, 1f), new Color(.07f, .22f, .23f, 1f)));
            button.AddThemeStyleboxOverride("hover", MakeStyle(new Color(.25f, .62f, .64f, 1f), new Color(.80f, .92f, .86f, 1f)));
            button.AddThemeStyleboxOverride("pressed", MakeStyle(new Color(.12f, .38f, .40f, 1f), new Color(.05f, .16f, .17f, 1f)));
            button.AddThemeColorOverride("font_color", new Color(.96f, .93f, .77f, 1f));
            button.AddThemeColorOverride("font_hover_color", new Color(1f, .99f, .88f, 1f));
            button.AddThemeFontSizeOverride("font_size", 28);
            button.Pressed += TogglePreview;
            button.GuiInput += DragWithRightMouse;
            return button;
        }

        private static StyleBoxFlat MakeStyle(Color background, Color border)
        {
            return new StyleBoxFlat
            {
                BgColor = background,
                BorderColor = border,
                BorderWidthLeft = 3,
                BorderWidthTop = 3,
                BorderWidthRight = 3,
                BorderWidthBottom = 3,
                CornerRadiusTopLeft = 12,
                CornerRadiusTopRight = 12,
                CornerRadiusBottomLeft = 12,
                CornerRadiusBottomRight = 12,
                ShadowColor = new Color(0f, 0f, 0f, .45f),
                ShadowSize = 5,
                ContentMarginLeft = 18,
                ContentMarginRight = 18
            };
        }

        private void TogglePreview()
        {
            if (_isPreviewing)
            {
                RestoreMap();
                return;
            }

            try
            {
                Control[] roots = GetMapLayerRoots();
                if (roots.Length == 0)
                {
                    _button.Text = "地图未准备好";
                    return;
                }

                Rect2 bounds = GetMapBounds(roots);
                if (bounds.Size.X <= 0f || bounds.Size.Y <= 0f)
                {
                    _button.Text = "地图尺寸不可用";
                    return;
                }

                Rect2 viewport = _screen.GetViewport().GetVisibleRect();
                _previewZoom = Math.Min(1f, Math.Min(viewport.Size.X * .88f / bounds.Size.X, viewport.Size.Y * .80f / bounds.Size.Y));
                _previewOrigin = bounds.Position;
                _previewSize = bounds.Size;

                RememberNormalMapPosition(roots);

                _clipContents = _screen.ClipContents;
                _screen.ClipContents = false;
                _isPreviewing = true;
                _button.Text = "返回正常地图";
                KeepPreviewFitted();
            }
            catch
            {
                _button.Text = "预览失败";
            }
        }

        internal void KeepPreviewFitted()
        {
            if (!_isPreviewing || _layers == null)
            {
                return;
            }

            Rect2 viewport = _screen.GetViewport().GetVisibleRect();
            Vector2 center = (viewport.Size - _previewSize * _previewZoom) / 2f;
            foreach (LayerState layer in _layers)
            {
                layer.ApplyPreview(_previewZoom, center, _previewOrigin);
            }

            AlignDrawingsWithMapBackground();
        }

        internal void PrepareDrawingInput(NMapDrawingInput input)
        {
            if (!_isPreviewing)
            {
                return;
            }

            Rect2 viewport = _screen.GetViewport().GetVisibleRect();
            input.Position = Vector2.Zero;
            input.Size = viewport.Size;
            input.Scale = Vector2.One;
            input.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        internal void ReplaceWithPreviewPointer(NMapDrawings drawings, ref Vector2 position)
        {
            if (!_isPreviewing)
            {
                return;
            }

            position = drawings.GetGlobalTransformWithCanvas().AffineInverse() * _screen.GetViewport().GetMousePosition();
        }

        internal void RememberNormalMapPosition()
        {
            if (!_isPreviewing)
            {
                RememberNormalMapPosition(GetMapLayerRoots());
            }
        }

        private void RememberNormalMapPosition(Control[] roots)
        {
            _layers = new LayerState[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                _layers[i] = new LayerState(roots[i]);
            }
        }

        private void AlignDrawingsWithMapBackground()
        {
            var drawings = DrawingsProperty.GetValue(_screen) as Control;
            var mapBackground = LayerFields[3].GetValue(_screen) as Control;
            if (drawings == null || mapBackground == null)
            {
                return;
            }

            drawings.GlobalPosition = mapBackground.GlobalPosition;
            drawings.Scale = mapBackground.Scale;
            drawings.Size = mapBackground.Size;
        }

        internal void RestoreMap()
        {
            if (!_isPreviewing)
            {
                return;
            }

            foreach (LayerState layer in _layers)
            {
                layer.Restore();
            }

            _screen.ClipContents = _clipContents;
            _isPreviewing = false;
            _button.Text = "查看地图";
        }

        private Control[] GetMapLayerRoots()
        {
            var candidates = new Control[LayerFields.Length + 1];
            var roots = new Control[LayerFields.Length + 1];
            int candidateCount = 0;
            int rootCount = 0;

            foreach (FieldInfo field in LayerFields)
            {
                var control = field.GetValue(_screen) as Control;
                if (control != null)
                {
                    candidates[candidateCount++] = control;
                }
            }

            var drawings = DrawingsProperty.GetValue(_screen) as Control;
            if (drawings != null)
            {
                candidates[candidateCount++] = drawings;
            }

            for (int index = 0; index < candidateCount; index++)
            {
                bool hasMapParent = false;
                for (int parentIndex = 0; parentIndex < candidateCount; parentIndex++)
                {
                    if (index != parentIndex && IsDescendantOf(candidates[index], candidates[parentIndex]))
                    {
                        hasMapParent = true;
                        break;
                    }
                }

                if (!hasMapParent)
                {
                    roots[rootCount++] = candidates[index];
                }
            }

            Array.Resize(ref roots, rootCount);
            return roots;
        }

        private Rect2 GetMapBounds(Control[] roots)
        {
            Rect2 bounds = new Rect2(roots[0].Position, roots[0].Size);
            for (int i = 1; i < roots.Length; i++)
            {
                bounds = bounds.Expand(roots[i].Position);
                bounds = bounds.Expand(roots[i].Position + roots[i].Size);
            }

            ExpandWithMapPoint(ref bounds, StartingPointField.GetValue(_screen) as NMapPoint);
            ExpandWithMapPoint(ref bounds, BossPointField.GetValue(_screen) as NMapPoint);
            ExpandWithMapPoint(ref bounds, SecondBossPointField.GetValue(_screen) as NMapPoint);

            var entries = MapPointDictionaryField.GetValue(_screen) as IEnumerable;
            if (entries != null)
            {
                foreach (object entry in entries)
                {
                    var value = entry.GetType().GetProperty("Value").GetValue(entry) as NMapPoint;
                    ExpandWithMapPoint(ref bounds, value);
                }
            }

            return bounds;
        }

        private static void ExpandWithMapPoint(ref Rect2 bounds, NMapPoint point)
        {
            if (point == null)
            {
                return;
            }

            bounds = bounds.Expand(point.GlobalPosition);
            bounds = bounds.Expand(point.GlobalPosition + point.Size);
        }

        private static bool IsDescendantOf(Node node, Node possibleParent)
        {
            for (Node parent = node.GetParent(); parent != null; parent = parent.GetParent())
            {
                if (ReferenceEquals(parent, possibleParent))
                {
                    return true;
                }
            }

            return false;
        }

        private void DragWithRightMouse(InputEvent input)
        {
            var buttonEvent = input as InputEventMouseButton;
            if (buttonEvent != null && buttonEvent.ButtonIndex == MouseButton.Right)
            {
                _isRightDragging = buttonEvent.Pressed;
                return;
            }

            var motionEvent = input as InputEventMouseMotion;
            if (_isRightDragging && motionEvent != null)
            {
                _button.Position += motionEvent.Relative;
                _savedButtonPosition = _button.Position;
                _hasSavedButtonPosition = true;
            }
        }

        internal static bool IsPreviewing(NMapScreen screen)
        {
            var overlay = Find(screen);
            return overlay != null && overlay._isPreviewing;
        }

        internal static MapPreviewOverlay Find(NMapScreen screen)
        {
            return screen == null ? null : screen.GetNodeOrNull(new NodePath(NodeName)) as MapPreviewOverlay;
        }

        internal static MapPreviewOverlay FindFor(Node node)
        {
            for (Node parent = node; parent != null; parent = parent.GetParent())
            {
                var screen = parent as NMapScreen;
                if (screen != null)
                {
                    return Find(screen);
                }
            }

            return null;
        }

        private static FieldInfo Field(string name)
        {
            return AccessTools.Field(typeof(NMapScreen), name);
        }

        private sealed class LayerState
        {
            private readonly Control _control;
            private readonly Vector2 _position;
            private readonly Vector2 _scale;

            internal LayerState(Control control)
            {
                _control = control;
                _position = control.Position;
                _scale = control.Scale;
            }

            internal void Restore()
            {
                _control.Position = _position;
                _control.Scale = _scale;
            }

            internal void ApplyPreview(float zoom, Vector2 center, Vector2 origin)
            {
                _control.Scale = _scale * zoom;
                _control.Position = center + (_position - origin) * zoom;
            }
        }
    }
}
