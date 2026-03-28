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
        private static bool IsDataStale = true;

        public static void Initialize(NetLobbyScreen lobby)
        {
            if (TabButton != null) return;

            WITGLogger.Log("[WITG] UI: Initializing for lobby.");
            var chatBoxField = typeof(NetLobbyScreen).GetField("chatBox", BindingFlags.NonPublic | BindingFlags.Instance);
            var chatBox = chatBoxField?.GetValue(lobby) as GUIListBox;
            var container = chatBox?.Parent?.Parent?.Parent;

            if (container == null)
            {
                WITGLogger.Error("[WITG] Critical Error: Could not find logHolderBottom container!");
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
                    IsDataStale = true;
                    Refresh();
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

            allTabs?.ForEach(t =>
            {
                if (t == TabButton) return;
                var oldOnClicked = t.OnClicked;
                t.OnClicked = (b, u) =>
                {
                    if (MainPanel != null) MainPanel.Visible = false;
                    return oldOnClicked?.Invoke(b, u) ?? true;
                };
            });

            if (lobby.LogButtons.CountChildren > 0)
            {
                float share = 1.0f / lobby.LogButtons.CountChildren;
                foreach (var child in lobby.LogButtons.Children) child.RectTransform.RelativeSize = new Vector2(share, 1.0f);
            }
        }

        public static void Refresh()
        {
            WITGLogger.Log("[WITG] UI: Refreshing identity lists.");
            IsDataStale = false;
            PopulateList(ListBox);
            PopulateList(FloatingListBox);
            UpdateActionButton();
        }

        private static void PopulateList(GUIListBox? targetList)
        {
            if (targetList == null) return;
            WITGLogger.Log($"[WITG] UI: Populating list (Count: {IdentityData.Cache.Count})");
            targetList.Deselect();
            targetList.Content.ClearChildren();

            if (IsDataStale)
            {
                _ = new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), targetList.Content.RectTransform),
                    TextManager.Get("Loading") ?? "Loading...", font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                int slotIndex = i;
                bool isActive = i == IdentityData.ActiveSlot;
                bool hasData = IdentityData.Cache.TryGetValue(slotIndex, out var data);

                var row = new GUIButton(new RectTransform(new Vector2(1f, 0.15f), targetList.Content.RectTransform), style: "ListBoxElement")
                {
                    UserData = slotIndex,
                    Color = isActive ? Color.Gold * 0.15f : Color.Black * 0.3f,
                    OnClicked = (btn, obj) =>
                    {
                        SelectedUISlot = slotIndex;

                        if (targetList == ListBox)
                        {
                            if (!isActive)
                            {
                                WITGLogger.Log($"[WITG] UI: Selected slot {slotIndex} from Main List.");
                                IdentityNetworking.SelectSlot(slotIndex);
                            }
                        }
                        else
                        {
                            WITGLogger.Log($"[WITG] UI: Selecting slot {slotIndex} in Floating List (not sending yet).");
                            targetList.Select(slotIndex);
                            UpdateActionButton();
                        }
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
                                WITGLogger.Log($"[WITG] UI: Opening delete confirmation for slot {slotIndex} ({data.Name}).");
                                var confirm = new GUIMessageBox(
                                    TextSOS.Get("witg.deleteidentity", "DELETE IDENTITY"),
                                    TextSOS.Get("witg.confirmdeleteidentity", "Are you sure you want to delete [name]?").Replace("[name]", data.Name) +
                                    "\n\n" + TextSOS.Get("witg.deletewarning", "(Note: This will be permanent only after the next campaign save)"),
                                    [TextManager.Get("Yes"), TextManager.Get("No")]);

                                if (targetList == FloatingListBox && FloatingPanel != null)
                                {
                                    GUIMessageBox.MessageBoxes.Remove(confirm);
                                    confirm.RectTransform.Parent = FloatingPanel.RectTransform;
                                    confirm.RectTransform.SetAsLastChild();
                                }

                                confirm.Buttons[0].OnClicked = (b, u) => {
                                    WITGLogger.Log($"[WITG] UI: Confirmed delete for slot {slotIndex}. Sending network request.");
                                    IdentityData.Cache.Remove(slotIndex);
                                    IdentityNetworking.SendDeleteSlot(slotIndex);
                                    confirm.Close();
                                    Refresh();
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

                    Color nameColor;
                    string displayName = "";

                    if (data.IsPermanentlyDead)
                    {
                        nameColor = GUIStyle.Red;
                        displayName = $"{data.Name} - [{TextSOS.Get("witg.dead", "DEAD")}]";
                    }
                    else if (data.IsWounded)
                    {
                        nameColor = GUIStyle.Orange;
                        displayName = $"{data.Name} - [{TextSOS.Get("witg.wounded", "WOUNDED")}]";
                    }
                    else
                    {
                        nameColor = Color.White;
                        displayName = data.Name;
                    }

                    _ = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), info.RectTransform), displayName, font: GUIStyle.SubHeadingFont, textColor: nameColor) { AutoScaleHorizontal = true, CanBeFocused = false };

                    _ = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), info.RectTransform), (data.Job ?? "").ToUpper(), font: GUIStyle.SmallFont, textColor: job?.UIColor ?? Color.LightBlue) { CanBeFocused = false };

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
                WITGLogger.Log("[WITG] UI: Closing Floating Panel.");
                GUIMessageBox.MessageBoxes.Remove(FloatingPanel);
                FloatingPanel = null;
                FloatingListBox = null;
                ActionButton = null;
                SelectedUISlot = -1;
                return;
            }

            WITGLogger.Log("[WITG] UI: Opening Floating Panel.");

            //var panelHolder = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null);
            FloatingPanel = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.6f), GUI.Canvas, Anchor.Center), style: "GUIFrame") { CanBeFocused = true };

            var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), FloatingPanel.RectTransform, Anchor.Center)) { Stretch = true, AbsoluteSpacing = GUI.IntScale(5) };

            var header = new GUIFrame(new RectTransform(new Vector2(1f, 0.1f), layout.RectTransform), style: "GUISlopedHeader") { Color = Color.Gold * 0.6f };
            _ = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1f), header.RectTransform, Anchor.CenterLeft), TextSOS.Get("witg.crewmanifest", "CREW MANIFEST"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);
            _ = new GUIButton(new RectTransform(new Vector2(0.10f, 0.8f), header.RectTransform, Anchor.CenterRight), style: "GUICancelButton") { OnClicked = (_, _) => { ToggleFloatingPanel(); return true; } };

            FloatingListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.75f), layout.RectTransform), style: "GUIListBox")
            {
                Spacing = GUI.IntScale(4)
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
                        WITGLogger.Log($"[WITG] UI: ActionButton clicked. Selecting slot {SelectedUISlot}.");
                        IdentityNetworking.SelectSlot(SelectedUISlot);
                    }
                    return true;
                }
            };

            GUIMessageBox.MessageBoxes.Add(FloatingPanel);
            IsDataStale = true;
            Refresh();
            IdentityNetworking.RequestInfo();
        }
    }
}