using System;
using System.IO;
using System.Globalization;
using KSP.UI.Screens;
using UnityEngine;

namespace PersistentFlight.Regions
{
    public sealed partial class RegionalFleetPanel
    {
        ApplicationLauncherButton toolbarButton;
        Texture2D toolbarIcon;
        bool uiHidden;
        Vector2 savedPosition;
        string SettingsPath { get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData/PersistentFlight/PluginData/MissionControl.cfg"); } }
        public void Awake()
        {
            LoadPosition();
            GameEvents.onGUIApplicationLauncherReady.Add(AddToolbar);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(RemoveToolbar);
            GameEvents.onHideUI.Add(HideUI);
            GameEvents.onShowUI.Add(ShowUI);
        }
        public void Start() { if(ApplicationLauncher.Ready)AddToolbar(); }
        void AddToolbar()
        {
            if(toolbarButton!=null || !ApplicationLauncher.Ready || ApplicationLauncher.Instance==null)return;
            if(toolbarIcon==null)toolbarIcon=CreateIcon();
            toolbarButton=ApplicationLauncher.Instance.AddModApplication(
                ()=>SetOpen(true),()=>SetOpen(false),null,null,null,null,
                ApplicationLauncher.AppScenes.FLIGHT|ApplicationLauncher.AppScenes.MAPVIEW,toolbarIcon);
            if(open)toolbarButton.SetTrue(false);
        }
        void RemoveToolbar()
        {
            if(toolbarButton!=null && ApplicationLauncher.Instance!=null)
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
            toolbarButton=null;
            InputLockManager.RemoveControlLock(InputLock);
        }
        void SetOpen(bool value)
        {
            open=value;
            if(toolbarButton!=null){if(value)toolbarButton.SetTrue(false);else toolbarButton.SetFalse(false);}
            if(!value){InputLockManager.RemoveControlLock(InputLock);SavePosition();}
        }
        void HideUI(){uiHidden=true;InputLockManager.RemoveControlLock(InputLock);}
        void ShowUI(){uiHidden=false;}
        void ClampWindow()
        {
            window.x=Mathf.Clamp(window.x,0,Math.Max(0,Screen.width-window.width));
            window.y=Mathf.Clamp(window.y,0,Math.Max(0,Screen.height-window.height));
        }
        void LoadPosition()
        {
            window.width=Math.Max(200,Math.Min(570,Screen.width-32));
            window.height=Math.Max(180,Math.Min(660,Screen.height-130));
            window.x=Math.Max(16,Screen.width-window.width-72);window.y=80;
            try {
                if(File.Exists(SettingsPath)){
                    var node=ConfigNode.Load(SettingsPath);float x,y;
                    if(node!=null && Single.TryParse(node.GetValue("x"),NumberStyles.Float,C,out x) &&
                        Single.TryParse(node.GetValue("y"),NumberStyles.Float,C,out y) &&
                        !Single.IsNaN(x) && !Single.IsInfinity(x) && !Single.IsNaN(y) && !Single.IsInfinity(y)){
                        window.x=x;window.y=y;
                    }
                }
            }catch(Exception e){Debug.LogWarning("[PF-UI] Could not read window position: "+e.Message);}
            ClampWindow();savedPosition=new Vector2(window.x,window.y);
        }
        void SavePosition()
        {
            var position=new Vector2(window.x,window.y);if(position==savedPosition)return;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var node=new ConfigNode();node.AddValue("x",window.x.ToString("R",C));node.AddValue("y",window.y.ToString("R",C));
                node.Save(SettingsPath);savedPosition=position;
            }catch(Exception e){Debug.LogWarning("[PF-UI] Could not save window position: "+e.Message);}
        }
        static Texture2D CreateIcon()
        {
            const int size=38;var pixels=new Color[size*size];
            // Small PF monogram drawn directly into the stock toolbar texture.
            string[] glyphs={"11110","10001","10001","11110","10000","10000","10000",
                             "11111","10000","10000","11110","10000","10000","10000"};
            for(int letter=0;letter<2;letter++)for(int row=0;row<7;row++)for(int col=0;col<5;col++)
                if(glyphs[letter*7+row][col]=='1')for(int dx=0;dx<2;dx++)for(int dy=0;dy<2;dy++)
                    pixels[(25-row*2+dy)*size+5+letter*16+col*2+dx]=new Color(.25f,.85f,1,1);
            for(int x=5;x<34;x++)pixels[8*size+x]=Color.white;
            var icon=new Texture2D(size,size,TextureFormat.RGBA32,false);icon.name="PersistentFlight mission control";
            icon.SetPixels(pixels);icon.Apply();return icon;
        }
        public void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddToolbar);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(RemoveToolbar);
            GameEvents.onHideUI.Remove(HideUI);GameEvents.onShowUI.Remove(ShowUI);
            SavePosition();RemoveToolbar();if(toolbarIcon!=null)Destroy(toolbarIcon);
        }
    }
}
