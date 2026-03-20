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
        private static GUIFrame? MainPanel;
        private static GUIListBox? ListBox;
        private static GUIButton? TabButton;

        public static void Initialize(NetLobbyScreen lobby)
        {
            if (TabButton != null) return;

            var chatBox = typeof(NetLobbyScreen).GetField("chatBox", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(lobby) as GUIListBox;
            var container = chatBox?.Parent?.Parent?.Parent; // logHolderBottom
            if (container == null) return;

            MainPanel = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform, Anchor.Center), style: null) { Visible = false };
            var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), MainPanel.RectTransform, Anchor.Center)) { Stretch = true, AbsoluteSpacing = GUI.IntScale(5) };

            var header = new GUIFrame(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), style: "GUISlopedHeader") { Color = Color.Gold * 0.6f };
            _ = new GUITextBlock(new RectTransform(Vector2.One, header.RectTransform), TextManager.Get("crewmanifestheader"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);

            ListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.88f), layout.RectTransform), style: "GUIListBox") { Spacing = GUI.IntScale(4) };

            TabButton = new GUIButton(new RectTransform(Vector2.One, lobby.LogButtons.RectTransform), TextSOS.Get("witg.tab.identities", "IDENTITIES"), style: "GUITabButton")
            {
                OnClicked = (btn, _) =>
                {
                    IdentityNetworking.RequestInfo();
                    foreach (var child in container.Children) child.Visible = (child == MainPanel);
                    var tabs = typeof(NetLobbyScreen).GetField("chatPanelTabButtons", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(lobby) as System.Collections.Generic.List<GUIButton>;
                    tabs?.ForEach(t => t.Selected = (t == btn));
                    return true;
                }
            };

            var allTabs = typeof(NetLobbyScreen).GetField("chatPanelTabButtons", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(lobby) as System.Collections.Generic.List<GUIButton>;
            allTabs?.Add(TabButton);

            float share = 1.0f / lobby.LogButtons.CountChildren;
            foreach (var child in lobby.LogButtons.Children) child.RectTransform.RelativeSize = new Vector2(share, 1.0f);

            allTabs?.ForEach(t =>
            {
                if (t == TabButton) return;
                var old = t.OnClicked;
                t.OnClicked = (b, u) => { MainPanel.Visible = false; return old?.Invoke(b, u) ?? true; };
            });
        }

        public static void Refresh()
        {
            if (ListBox == null) return;
            ListBox.Content.ClearChildren();

            for (int i = 0; i < 4; i++)
            {
                int slotIndex = i;
                bool isActive = (i == IdentityData.ActiveSlot);
                bool hasData = IdentityData.Cache.TryGetValue(i, out var data);

                var row = new GUIButton(new RectTransform(new Point(ListBox.Content.Rect.Width, GUI.IntScale(65)), ListBox.Content.RectTransform),
                    style: "ListBoxElement")
                {
                    Color = isActive ? Color.Gold * 0.15f : Color.Black * 0.3f,
                    OnClicked = (btn, obj) =>
                    {
                        if (!isActive) IdentityNetworking.SelectSlot(slotIndex);
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
                            new(TextManager.Get("Delete"), isEnabled: true, onSelected: () => {
                                var confirm = new GUIMessageBox(TextSOS.Get("witg.deleteidentity", "DELETE IDENTITY"), TextSOS.Get("witg.confirmdeleteidentity", "Are you sure you want to delete [name]?").Replace("[name]", data.Name),
                                    [TextManager.Get("Yes"), TextManager.Get("No")]);

                                confirm.Buttons[0].OnClicked = (b, u) => {
                                    IdentityData.Cache.Remove(slotIndex);
                                    IdentityUI.Refresh();
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

                var h = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), row.RectTransform, Anchor.Center), isHorizontal: true)
                { Stretch = true, AbsoluteSpacing = GUI.IntScale(10), CanBeFocused = false };

                var iconFrame = new GUIFrame(new RectTransform(new Vector2(0.15f, 1f), h.RectTransform), style: null) { CanBeFocused = false };

                if (hasData)
                {
                    var job = JobPrefab.Prefabs.FirstOrDefault(jp => jp.Name.Value.Equals(data.Job, StringComparison.OrdinalIgnoreCase));
                    if (job?.Icon != null) _ = new GUIImage(new RectTransform(Vector2.One, iconFrame.RectTransform, Anchor.Center), job.Icon, scaleToFit: true) { Color = job.UIColor, CanBeFocused = false };

                    var info = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1f), h.RectTransform)) { Stretch = true, CanBeFocused = false };
                    _ = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), info.RectTransform), data.Name, font: GUIStyle.SubHeadingFont) { AutoScaleHorizontal = true, CanBeFocused = false };
                    _ = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), info.RectTransform), data.Job.ToUpper(), font: GUIStyle.SmallFont, textColor: job?.UIColor ?? Color.LightBlue) { CanBeFocused = false };

                    if (isActive)
                        _ = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), h.RectTransform), TextManager.Get("Active"), textColor: Color.Gold, textAlignment: Alignment.CenterRight, font: GUIStyle.SmallFont) { CanBeFocused = false };
                }
                else
                {
                    _ = new GUITextBlock(new RectTransform(new Vector2(0.75f, 1f), h.RectTransform), TextSOS.Get("witg.emptyslot", "[ EMPTY SLOT [num] ]").Replace("[num]", (i + 1).ToString()), textColor: Color.White * 0.3f, textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont) { CanBeFocused = false };
                }
            }
        }
    }
}