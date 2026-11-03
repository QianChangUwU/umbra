/* Umbra | (c) 2024 by Una              ____ ___        ___.
 * Licensed under the terms of AGPL-3  |    |   \ _____ \_ |__ _______ _____
 *                                     |    |   //     \ | __ \\_  __ \\__  \
 * https://github.com/una-xiv/umbra    |    |  /|  Y Y  \| \_\ \|  | \/ / __ \_
 *                                     |______//__|_|  /____  /|__|   (____  /
 *     Umbra is free software: you can redistribute  \/     \/             \/
 *     it and/or modify it under the terms of the GNU Affero General Public
 *     License as published by the Free Software Foundation, either version 3
 *     of the License, or (at your option) any later version.
 *
 *     Umbra UI is distributed in the hope that it will be useful,
 *     but WITHOUT ANY WARRANTY; without even the implied warranty of
 *     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *     GNU Affero General Public License for more details.
 */

using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.Game;
using Umbra.Widgets.System;

namespace Umbra;

internal partial class Toolbar
{
    private readonly Node _toolbarNode = UmbraDrawing.DocumentFrom("umbra.toolbar.xml").RootNode!;

    private float _cachedCenterWidth;
    private int   _appliedLeftWidth;
    private int   _appliedRightWidth;

    public Node? GetPanel(string id) =>
        id.StartsWith("aux")
            ? auxBars.FindOrMigrate(id)
            : _toolbarNode.FindById(id)
              ?? throw new InvalidOperationException($"Panel '{id}' not found.");

    public Node LeftPanel   => GetPanel("Left")!;
    public Node CenterPanel => GetPanel("Center")!;
    public Node RightPanel  => GetPanel("Right")!;

    private void RenderAuxBarNodes(ImDrawListPtr drawList)
    {
        foreach (var (auxBarNode, config) in auxBars.VisibleAuxBarPanels) {
            auxBarNode.ToggleClass("left-aligned", config.XAlign == "Left");
            auxBarNode.ToggleClass("right-aligned", config.XAlign == "Right");
            auxBarNode.ToggleClass("center-aligned", config.XAlign == "Center");
            auxBarNode.ToggleClass("top-aligned", config.YAlign == "Top");
            auxBarNode.ToggleClass("middle-aligned", config.YAlign == "Center");
            auxBarNode.ToggleClass("bottom-aligned", config.YAlign == "Bottom");

            Vector2 workPos  = ImGui.GetMainViewport().WorkPos;
            Vector2 workSize = ImGui.GetMainViewport().WorkSize;

            // The bounds are only stale when something actually invalidated the
            // layout. Skipping the recompute otherwise avoids a full-tree pass
            // every frame just to read a size that has not changed.
            if (auxBarNode.NeedsReflow) {
                auxBarNode.ComputeBoundingSize();
            }

            float xPos = config.XAlign switch {
                "Center" => (ToolbarXPosition - (auxBarNode.Bounds.MarginSize.Width / 2f)) + config.XPos,
                "Left"   => config.XPos,
                "Right"  => (int)(workPos.X + workSize.X - config.XPos - auxBarNode.Bounds.MarginSize.Width),
                _        => config.XPos,
            };

            float yPos = config.YAlign switch {
                "Center" => (workPos.Y + workSize.Y - (workSize.Y / 2f) - (auxBarNode.Bounds.MarginSize.Height / 2f)) + config.YPos,
                "Top"    => config.YPos,
                "Bottom" => (int)(workPos.Y + workSize.Y - config.YPos - auxBarNode.Bounds.MarginSize.Height),
                _        => config.YPos,
            };

            // Apply autohide effects for this auxbar if enabled
            float auxYOffset = 0;
            float auxOpacity = 1.0f;

            if (config.EnableAutoHide && ShouldAutoHide() && !config.IsConditionallyVisible) {
                auxYOffset = config.YAlign switch {
                    "Top"    => _autoHideYOffset,
                    "Bottom" => -_autoHideYOffset,
                    _        => 0,
                };

                if (IsMultiMonitorSupportEnabled()) {
                    auxOpacity = _autoHideOpacity;
                    auxYOffset = 0;
                } else {
                    auxOpacity = _autoHideOpacity;
                }
            }

            auxBarNode.Style.Opacity   = auxOpacity;
            auxBarNode.Style.IsVisible = auxOpacity > 0.01f;

            auxBarNode.Render(drawList, new(xPos, yPos + auxYOffset));
        }
    }

    /// <summary>
    /// Renders the toolbar.
    /// </summary>
    private void RenderToolbarNode(ImDrawListPtr drawList)
    {
        _toolbarNode.Style.Gap     = ItemSpacing * 2;
        _toolbarNode.Style.Opacity = player.IsEditingHud ? 0.66f : _autoHideOpacity;
        LeftPanel.Style.Gap        = ItemSpacing;
        CenterPanel.Style.Gap      = ItemSpacing;
        RightPanel.Style.Gap       = ItemSpacing;

        if (_toolbarNode.ClassList.Contains("stretched")) {
            ApplyStretchedPanelWidths();
        } else {
            ResetPanelWidths();

            LeftPanel.Style.AutoSize   = (AutoSize.Fit, AutoSize.Grow);
            CenterPanel.Style.AutoSize = (AutoSize.Fit, AutoSize.Grow);
            RightPanel.Style.AutoSize  = (AutoSize.Fit, AutoSize.Grow);
        }

        _toolbarNode.Render(drawList, new(ToolbarXPosition, (int)(ToolbarYPosition + _autoHideYOffset)));

        if (_toolbarNode.ClassList.Contains("stretched")) {
            _cachedCenterWidth = CenterPanel.Bounds.MarginSize.Width;
        }
    }

    /// <summary>
    /// Sizes the left and right panels so that they reach the screen edges while
    /// leaving room for the center panel.
    /// </summary>
    /// <remarks>
    /// The center width is measured from the previous frame's layout instead of
    /// being recomputed up front. Laying out the panels separately only to throw
    /// that work away during the actual render costs a full tree pass per panel
    /// per frame; the one-frame lag is not observable.
    /// </remarks>
    private void ApplyStretchedPanelWidths()
    {
        float centerWidth = _cachedCenterWidth > 0 ? _cachedCenterWidth : CenterPanel.Bounds.MarginSize.Width;

        if (centerWidth <= 0) {
            ResetPanelWidths();
            LeftPanel.Style.AutoSize   = (AutoSize.Fit, AutoSize.Grow);
            CenterPanel.Style.AutoSize = (AutoSize.Fit, AutoSize.Grow);
            RightPanel.Style.AutoSize  = (AutoSize.Fit, AutoSize.Grow);
            return;
        }

        float centerX1 = ToolbarXPosition - (centerWidth / 2f);
        float centerX2 = ToolbarXPosition + (centerWidth / 2f);

        SetPanelWidth(LeftPanel, (int)centerX1 - 1, ref _appliedLeftWidth);
        SetPanelWidth(RightPanel, (int)(ImGui.GetMainViewport().WorkSize.X - centerX2) - 1, ref _appliedRightWidth);

        LeftPanel.Style.AutoSize   = (AutoSize.Fit, AutoSize.Grow);
        RightPanel.Style.AutoSize  = (AutoSize.Fit, AutoSize.Grow);
        CenterPanel.Style.AutoSize = (AutoSize.Fit, AutoSize.Grow);
    }

    private static void SetPanelWidth(Node panel, int width, ref int appliedWidth)
    {
        if (appliedWidth == width) return;

        appliedWidth          = width;
        panel.Style.Size      = new(width, 0);
    }

    private void ResetPanelWidths()
    {
        if (_appliedLeftWidth == 0 && _appliedRightWidth == 0) return;

        _appliedLeftWidth  = 0;
        _appliedRightWidth = 0;
        _cachedCenterWidth = 0;

        LeftPanel.Style.Size   = new();
        CenterPanel.Style.Size = new();
        RightPanel.Style.Size  = new();
    }

    /// <summary>
    /// Updates the width of the toolbar.
    /// </summary>
    private void UpdateToolbarWidth()
    {
        _toolbarNode.ToggleClass("stretched", IsStretched);

        float width = IsStretched ? ImGui.GetMainViewport().WorkSize.X : 0;

        _toolbarNode.Style.Size = new(width / Node.ScaleFactor, Height);
        _toolbarNode.Style.Padding = new() {
            Left  = (int)(ToolbarLeftMargin / Node.ScaleFactor),
            Right = (int)(ToolbarRightMargin / Node.ScaleFactor)
        };
    }

    /// <summary>
    /// Updates the class list of the toolbar node based on the current configuration.
    /// </summary>
    private void UpdateToolbarNodeClassList()
    {
        if (EnableInactiveColors) {
            if (!IsCursorNearToolbar() && !_toolbarNode.TagsList.Contains("blur")) {
                _toolbarNode.TagsList.Add("blur");
            } else if (IsCursorNearToolbar() && _toolbarNode.TagsList.Contains("blur")) {
                _toolbarNode.TagsList.Remove("blur");
            }
        } else {
            _toolbarNode.TagsList.Remove("blur");
        }

        _toolbarNode.ToggleClass("shadow", EnableShadow && (!EnableInactiveColors || IsCursorNearToolbar()));
        _toolbarNode.ToggleClass("top", IsTopAligned);
        _toolbarNode.ToggleClass("bottom", !IsTopAligned);
        _toolbarNode.ToggleClass("rounded", !IsStretched && RoundedCorners);
    }

    /// <summary>
    /// Opens the toolbar host window if UseWindowDrawList is enabled and returns its draw list.
    /// Returns false if using the background draw list (no window was opened).
    /// </summary>
    private static unsafe bool BeginWindowDrawList(out ImDrawListPtr drawList)
    {
        if (!UseWindowDrawList) {
            drawList = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
            Node.PassthroughDrawListHandles.Clear();
            return false;
        }

        var vp    = ImGui.GetMainViewport();
        var flags = ImGuiWindowFlags.NoTitleBar    | ImGuiWindowFlags.NoResize     | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoInputs      | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing
                  | ImGuiWindowFlags.NoDecoration  | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking
                  | ImGuiWindowFlags.NoNavFocus    | ImGuiWindowFlags.NoNavInputs   | ImGuiWindowFlags.NoCollapse
                  | ImGuiWindowFlags.NoMove        | ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.SetNextWindowPos(vp.WorkPos);
        ImGui.SetNextWindowSize(vp.WorkSize);
        ImGui.SetNextWindowViewport(vp.ID);
        ImGui.Begin("##UmbraToolbarHost", flags);
        ImGui.PopStyleVar(2);
        ImGui.SetCursorScreenPos(Vector2.Zero);
        drawList = ImGui.GetWindowDrawList();
        Node.PassthroughDrawListHandles.Clear();
        Node.PassthroughDrawListHandles.Add((nint)drawList.Handle);
        return true;
    }

    /// <summary>
    /// Returns the horizontal position of the toolbar.
    /// </summary>
    private static float ToolbarXPosition =>
        (ImGui.GetMainViewport().WorkPos.X + (ImGui.GetMainViewport().WorkSize.X / 2));

    /// <summary>
    /// Returns the vertical position of the toolbar.
    /// </summary>
    private static float ToolbarYPosition =>
        IsTopAligned
            ? ImGui.GetMainViewport().WorkPos.Y + YOffset
            : ImGui.GetMainViewport().WorkPos.Y + (int)ImGui.GetMainViewport().WorkSize.Y - YOffset;
}
