using System;
using System.Collections.Generic;
using UnhollowerBaseLib;
using UnhollowerRuntimeLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NpcMonthActivity
{
    internal sealed class ActivityUi
    {
        private UINPCInfo owner;
        private GameObject panel;
        private Text label, status;
        private readonly ModMain runtime;
        private string nativeDiagnostic="";
        private DateTime lastNativeSample=DateTime.MinValue;
        public ActivityUi(ModMain runtime) { this.runtime = runtime; }

        private static GameObject RectObject(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return go;
        }
        private static Text AddText(GameObject go, Font font, int size)
        {
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = new Color(1f, 0.94f, 0.82f, 1);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        public void Update()
        {
            var ui = g.ui.GetUI<UINPCInfo>(UIType.NPCInfo);
            if (ui == null || ui.unit == null)
            {
                Clear();
                return;
            }
            if (owner == null || owner.Pointer != ui.Pointer || panel == null)
            {
                Clear();
                owner = ui;
                panel = RectObject("NpcMonthActivity.Panel", ui.transform, new Vector2(340, 224), new Vector2(28, 24));
                var bg = panel.AddComponent<Image>();
                bg.color = new Color(0.08f, 0.12f, 0.14f, 0.96f);
                var buttonGo = RectObject("Toggle", panel.transform, new Vector2(312, 40), new Vector2(14, 174));
                var image = buttonGo.AddComponent<Image>();
                image.color = new Color(0.20f, 0.35f, 0.34f, 1);
                var button = buttonGo.AddComponent<Button>();
                button.targetGraphic = image;
                var font = ui.textTitle1.font;
                label = AddText(RectObject("Label", buttonGo.transform, new Vector2(312, 40), Vector2.zero), font, 22);
                status = AddText(RectObject("Status", panel.transform, new Vector2(320, 166), new Vector2(10, 4)), font, 14);
                button.onClick.AddListener((UnityAction)(Action)Toggle);
            }
            string id = ui.unit.data.unitData.unitID;
            bool player = id == g.world.playerUnit.data.unitData.unitID;
            panel.SetActive(!player);
            if (player) return;
            if((DateTime.UtcNow-lastNativeSample).TotalSeconds>=1)
            {
            lastNativeSample=DateTime.UtcNow;
            var nativeLines=new List<string>();
            var texts=ui.GetComponentsInChildren<Text>(false);
            for(int i=0;i<texts.Length && nativeLines.Count<16;i++)
            {
                var t=texts[i];
                if(t==null || t.Pointer==status?.Pointer || t.Pointer==label?.Pointer) continue;
                string value=t.text;
                if(!string.IsNullOrEmpty(value) && (value.Contains("瓶颈") || value.Contains("修为") || value.Contains("突破") || value.Contains("丹药")))
                    nativeLines.Add(Short(value.Replace("\n"," "),160));
            }
            string diagnostic=id+":"+ui.unit.data.dynUnitData.exp.value+":"+string.Join("|",nativeLines);
            if(diagnostic!=nativeDiagnostic)
            {
                nativeDiagnostic=diagnostic;
                ModMain.Log(id+" npc-ui-state name="+ui.unit.data.unitData.propertyData.GetName()+" texts="+string.Join("|",nativeLines));
            }
            }
            bool enabled = runtime.Engine.Enabled(id);
            label.text = enabled ? "取消 AI" : "赋予 AI";
            if (enabled)
            {
                var state = runtime.Engine.State.Npcs[id];
                status.text = Short(state.Status,60) + "\n余时 " + state.Budget.ToString("F2") + " 天 · " + GameAdapter.Read(ui.unit.data.unitData.GetPoint());
                if (state.Plan != null) status.text += "\n" + Short(state.Plan.Reason,55);
                if(!string.IsNullOrEmpty(state.SupplyStatus) && (state.Plan==null || state.Plan.Kind!=NeedKind.Purchase)) status.text+="\n丹药："+Short(state.SupplyStatus,45);
                if(!string.IsNullOrEmpty(state.LastEvent) && state.LastEvent!=state.Status) status.text+="\n最近："+Short(state.LastEvent,60);
            }
            else
            {
                status.text="月内活动：未启用（可被动参与交互）";
                string recent;
                if(runtime.Engine.State.RecentEvents.TryGetValue(id,out recent)) status.text+="\n最近："+Short(recent,90);
            }
        }
        private static string Short(string value,int length) { return value==null ? "" : value.Length<=length ? value : value.Substring(0,length)+"…"; }

        private void Toggle()
        {
            try
            {
                if (owner == null || owner.unit == null) return;
                string id = owner.unit.data.unitData.unitID;
                if (id == g.world.playerUnit.data.unitData.unitID) return;
                runtime.Toggle(id);
                Update();
            }
            catch (Exception ex) { ModMain.Log("toggle-error " + ex); }
        }

        public void Clear()
        {
            if (panel != null) Object.Destroy(panel);
            panel = null; owner = null; label = null; status = null;
        }
    }
}
