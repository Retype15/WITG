// Copyright (c) 2026 Retype15
// This file is licensed under the GNU GPLv3.
// See the LICENSE file in the project root for details.

#pragma warning disable IDE0130
#pragma warning disable IDE0079
#pragma warning disable IDE0290

using Barotrauma;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace WITG
{
    public static class IdentityUI
    {
        public static GUIFrame? MainPanel { get; set; }
        private static GUIListBox? ListBox;
        private static GUIButton? TabButton;
        public static GUIFrame? FloatingPanel { get; private set; }
        private static GUIListBox? FloatingListBox;

        private static GUIButton? ActionButton;
        private static int SelectedUISlot = -1;

        public static void Initialize(NetLobbyScreen lobby)
        {
            if (TabButton != null) return;

            var chatBoxField = typeof(NetLobbyScreen).GetField("chatBox", BindingFlags.NonPublic | BindingFlags.Instance);
            var chatBox = chatBoxField?.GetValue(lobby) as GUIListBox;

            var container = chatBox?.Parent?.Parent?.Parent;

            if (container == null)
            {
                LuaCsLogger.LogError("[WITG] Critical Error: Could not find logHolderBottom container!");
                return;
            }

            MainPanel = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform, Anchor.Center), style: "InnerFrame") { Visible = false };

            var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), MainPanel.RectTransform, Anchor.Center))
            {
                Stretch = true,
                AbsoluteSpacing = GUI.IntScale(5)
            };

            var header = new GUIFrame(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), style: "GUISlopedHeader") { Color = Color.Gold * 0.6f };

            _ = new GUITextBlock(new RectTransform(Vector2.One, header.RectTransform), TextSOS.Get("witg.crewmanifest", "CREW MANIFEST"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);

            ListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.88f), layout.RectTransform), style: "GUIListBox") { Spacing = GUI.IntScale(4) };

            TabButton = new GUIButton(new RectTransform(Vector2.One, lobby.LogButtons.RectTransform), TextSOS.Get("witg.tab.identities", "IDENTITIES"), style: "GUITabButton")
            {
                OnClicked = (btn, _) =>
                {
                    IdentityNetworking.RequestInfo();
                    foreach (var child in container.Children)
                    {
                        child.Visible = (child == MainPanel);
                    }

                    var tabsField = typeof(NetLobbyScreen).GetField("chatPanelTabButtons", BindingFlags.NonPublic | BindingFlags.Instance);
                    var tabs = tabsField?.GetValue(lobby) as List<GUIButton>;
                    tabs?.ForEach(t => t.Selected = (t == btn));
                    return true;
                }
            };

            var allTabsField = typeof(NetLobbyScreen).GetField("chatPanelTabButtons", BindingFlags.NonPublic | BindingFlags.Instance);
            var allTabs = allTabsField?.GetValue(lobby) as List<GUIButton>;
            allTabs?.Add(TabButton);

            if (lobby.LogButtons.CountChildren > 0)
            {
                float share = 1.0f / lobby.LogButtons.CountChildren;
                foreach (var child in lobby.LogButtons.Children) child.RectTransform.RelativeSize = new Vector2(share, 1.0f);
            }
        }

        public static void Refresh()
        {
            PopulateList(ListBox);
            PopulateList(FloatingListBox);
            if (FloatingPanel != null && SelectedUISlot != -1)
            {
                UpdateActionButton();
            }
        }

        private static void PopulateList(GUIListBox? targetList)
        {
            if (targetList == null) return;
            targetList.Content.ClearChildren();

            for (int i = 0; i < 4; i++)
            {
                int slotIndex = i;
                bool isActive = i == IdentityData.ActiveSlot;
                bool hasData = IdentityData.Cache.TryGetValue(slotIndex, out var data);

                var row = new GUIButton(new RectTransform(new Point(targetList.Content.Rect.Width, GUI.IntScale(65)), targetList.Content.RectTransform), style: "ListBoxElement")
                {
                    UserData = slotIndex,
                    Color = isActive ? Color.Gold * 0.15f : Color.Black * 0.3f,
                    OnClicked = (btn, obj) =>
                    {
                        targetList.Select(slotIndex);
                        SelectedUISlot = slotIndex;

                        if (targetList == ListBox && !isActive)
                            IdentityNetworking.SelectSlot(slotIndex);

                        if (targetList == FloatingListBox)
                            UpdateActionButton();

                        return true;
                    },
                    OnSecondaryClicked = (btn, obj) =>
                    {
                        if (!hasData) return false;
                        if (slotIndex == 0)
                        {
                            GUI.AddMessage(TextSOS.Get("witg.error.primaryidnodelete", "The primary identity cannot be deleted."), Color.Orange);
                            return false;
                        }
                        var options = new ContextMenuOption[] {
                        new(TextManager.Get("Delete"), isEnabled: true, onSelected: () =>
                        {
                            var confirm = new GUIMessageBox(TextSOS.Get("witg.deleteidentity", "DELETE IDENTITY"),
                                TextSOS.Get("witg.confirmdeleteidentity", "Are you sure you want to delete [name]?").Replace("[name]", data.Name),
                                [TextManager.Get("Yes"), TextManager.Get("No")]);

                            confirm.Buttons[0].OnClicked = (b, u) => {
                                IdentityData.Cache.Remove(slotIndex);
                                Refresh();
                                IdentityNetworking.SendDeleteSlot(slotIndex);
                                confirm.Close();
                                return true;
                            };
                            confirm.Buttons[1].OnClicked = confirm.Close;
                        })
                        };
                        GUIContextMenu.CreateContextMenu(null, data.Name, Color.Red, options);
                        return true;
                    }
                };

                var h = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), row.RectTransform, Anchor.Center), isHorizontal: true) { Stretch = true, AbsoluteSpacing = GUI.IntScale(10), CanBeFocused = false };
                var iconFrame = new GUIFrame(new RectTransform(new Vector2(0.15f, 1f), h.RectTransform), style: null) { CanBeFocused = false };

                if (hasData)
                {
                    var job = JobPrefab.Prefabs.FirstOrDefault(jp => jp.Name.Value.Equals(data.Job, StringComparison.OrdinalIgnoreCase));
                    if (job?.Icon != null) _ = new GUIImage(new RectTransform(Vector2.One, iconFrame.RectTransform, Anchor.Center), job.Icon, scaleToFit: true) { Color = job.UIColor, CanBeFocused = false };

                    var info = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1f), h.RectTransform)) { Stretch = true, CanBeFocused = false };

                    Color nameColor = data.IsDead ? GUIStyle.Red : Color.White;
                    string displayName = data.IsDead ? $"[DEAD] {data.Name}" : data.Name;

                    _ = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), info.RectTransform), displayName, font: GUIStyle.SubHeadingFont, textColor: nameColor) { AutoScaleHorizontal = true, CanBeFocused = false };
                    _ = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), info.RectTransform), data.Job.ToUpper(), font: GUIStyle.SmallFont, textColor: job?.UIColor ?? Color.LightBlue) { CanBeFocused = false };

                    if (isActive) _ = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), h.RectTransform), TextManager.Get("Active"), textColor: Color.Gold, textAlignment: Alignment.CenterRight, font: GUIStyle.SmallFont) { CanBeFocused = false };
                }
                else
                {
                    _ = new GUITextBlock(new RectTransform(new Vector2(0.75f, 1f), h.RectTransform), TextSOS.Get("witg.emptyslot", "[ EMPTY SLOT [num] ]").Replace("[num]", (i + 1).ToString()), textColor: Color.White * 0.3f, textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont) { CanBeFocused = false };
                }
            }
        }

        private static void UpdateActionButton()
        {
            if (ActionButton == null) return;

            if (SelectedUISlot == -1)
            {
                ActionButton.Text = TextSOS.Get("witg.selectaslot", "SELECT A SLOT");
                ActionButton.Enabled = false;
            }
            else if (SelectedUISlot == IdentityData.ActiveSlot)
            {
                ActionButton.Text = TextManager.Get("Active").Value;
                ActionButton.Enabled = false;
            }
            else
            {
                ActionButton.Text = TextSOS.Get("witg.button.select", "SELECT IDENTITY");
                ActionButton.Enabled = true;
            }
        }

        public static void ToggleFloatingPanel()
        {
            if (FloatingPanel != null)
            {
                var parent = FloatingPanel.Parent ?? FloatingPanel;
                GUIMessageBox.MessageBoxes.Remove(parent);
                FloatingPanel = null;
                FloatingListBox = null;
                ActionButton = null;
                SelectedUISlot = -1;
                return;
            }

            var panelHolder = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null);
            FloatingPanel = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.55f), panelHolder.RectTransform, Anchor.Center), style: "GUIFrame") { CanBeFocused = true };

            var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), FloatingPanel.RectTransform, Anchor.Center)) { Stretch = true, AbsoluteSpacing = GUI.IntScale(5) };

            var header = new GUIFrame(new RectTransform(new Vector2(1f, 0.1f), layout.RectTransform), style: "GUISlopedHeader") { Color = Color.Gold * 0.6f };
            _ = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1f), header.RectTransform, Anchor.CenterLeft), TextSOS.Get("witg.crewmanifest", "CREW MANIFEST"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);
            _ = new GUIButton(new RectTransform(new Vector2(0.15f, 0.8f), header.RectTransform, Anchor.CenterRight), "X", style: "GUIButtonSmall") { OnClicked = (_, _) => { ToggleFloatingPanel(); return true; } };

            FloatingListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.75f), layout.RectTransform), style: "GUIListBox")
            {
                Spacing = GUI.IntScale(4),
                OnSelected = (comp, userdata) =>
                {
                    if (userdata is int slotIndex)
                    {
                        SelectedUISlot = slotIndex;
                        UpdateActionButton();
                    }
                    return true;
                }
            };

            var btnContainer = new GUIFrame(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), style: null);
            ActionButton = new GUIButton(new RectTransform(new Vector2(0.6f, 1f), btnContainer.RectTransform, Anchor.Center),
                TextSOS.Get("witg.selectaslot", "SELECT A SLOT"), style: "GUIButton")
            {
                Enabled = false,
                OnClicked = (btn, obj) =>
                {
                    if (SelectedUISlot != -1)
                    {
                        IdentityNetworking.SelectSlot(SelectedUISlot);
                        ToggleFloatingPanel();
                    }
                    return true;
                }
            };

            GUIMessageBox.MessageBoxes.Add(panelHolder);
            Refresh();
            IdentityNetworking.RequestInfo();
        }
    }
}