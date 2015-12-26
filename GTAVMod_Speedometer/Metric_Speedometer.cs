/*
 * Simple Metric/Imperial Speedometer
 * Author: libertylocked
 * Version: 2.1.3
 * License: GPLv2
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using GTA;
using GTA.Math;
using GTA.Native;
using NativeUI;

namespace GTAVMod_Speedometer
{
    public class Metric_Speedometer : Script
    {
        // Constants
        public const string SCRIPT_VERSION = "2.1.3";
        const string URL_VERSIONFILE = @"https://raw.githubusercontent.com/LibertyLocked/GTAVMod_Speedometer/release/GTAVMod_Speedometer/version.txt"; // latest ver text
        const int NUM_FONTS = 8;
        const float RAINBOW_FRAMETIME = 0.034f;

        #region Fields

        bool creditsShown = false;
        UIContainer speedContainer, odometerContainer;
        UIText speedText, odometerText;
        SpeedoMode speedoMode;
        float distanceKm = 0;
        Vector3 prevPos;
        int rainbowHueBp = 0; // 0 to 10000
        float rainbowTimeCounter = 0;
        UpdateCheckState updateCheckState;
        string updateCheckResult;
        AccelerationTimerWidget wid_accTimer;
        MaxSpeedWidget wid_maxSpeed;

        ScriptSettings settings;
        bool enableMenu;
        Keys menuKey;
        bool enableSaving;
        bool useMph;
        int rainbowMode = 0;
		bool onfootSpeedo;

        // Fields for menus
        MenuPool menuPool;
        UIMenu coreMenu, dispMenu, colorMenu, extrasMenu, mainMenu;
        bool isChangingBackcolor;

        // Fields for UI settings
        VerticalAlignment vAlign;
        HorizontalAlign hAlign;
        Point posOffset;
        int pWidth, pHeight;
        float fontSize;
        int fontStyle;
        Color backcolor, forecolor;
        string kphText, mphText;

        #endregion

        public Metric_Speedometer()
        {
            ParseSettings();
            SetupUIElements();
            SetupMenus();

            this.Tick += OnTick;
            this.KeyDown += OnKeyDown;

            this.wid_accTimer = new AccelerationTimerWidget();
            this.wid_maxSpeed = new MaxSpeedWidget();
        }

        #region Event handles

        void OnTick(object sender, EventArgs e)
        {
            menuPool.ProcessMenus();

            if (enableSaving)
            {
                bool isPausePressed = Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 2, 199) ||
                    Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 2, 200); // pause or pause alternate button
                if (isPausePressed) SaveStats();
            }

            if (updateCheckState == UpdateCheckState.Checked)
            {
                updateCheckState = UpdateCheckState.Stopped;
                UI.Notify(updateCheckResult);
            }

            Player player = Game.Player;
            if (player != null && player.CanControlCharacter && player.IsAlive && player.Character != null)
            {
                // update and draw
                if (player.Character.IsInVehicle() || onfootSpeedo || Utils.IsPlayerRidingDeer(player.Character))  // conditions to draw speedo
                {
                    // in veh or riding deer
                    float speed = 0;
                    if (player.Character.IsInVehicle()) speed = player.Character.CurrentVehicle.Speed;
                    else if (onfootSpeedo || Utils.IsPlayerRidingDeer(player.Character)) speed = GetSpeedFromPosChange(player.Character);

                    Update(speed);
                    Draw();

                    wid_accTimer.Update(speed);
                    wid_accTimer.Draw();
                    wid_maxSpeed.Update(speed);
                    wid_maxSpeed.Draw(useMph);

                    // update rainbow
                    if (rainbowMode != 0)
                    {
                        rainbowTimeCounter += Game.LastFrameTime;
                        if (rainbowTimeCounter > RAINBOW_FRAMETIME)
                        {
                            rainbowTimeCounter = 0;
                            rainbowHueBp = (rainbowHueBp + (int)(1 * Math.Pow(2, rainbowMode - 1) * speed)) % 10000;
                            speedText.Color = Utils.HSLA2RGBA((double)rainbowHueBp / 10000, 1, 0.5, forecolor.A / 255.0);
                        }
                    }
                }
                else
                {
                    // not in veh, not riding deer
                    if (wid_accTimer.State != AccelerationTimerState.Off) wid_accTimer.Stop();
                    if (wid_maxSpeed.State != MaxSpeedState.Off) wid_maxSpeed.Stop();
                }
            }

            if (player != null && player.Character != null)
                prevPos = Game.Player.Character.Position;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (enableMenu && e.KeyCode == menuKey)
            {
                mainMenu.Visible = true;
                UpdateAllMenuButtons();
                
                if (!creditsShown)
                {
                    ShowCredits(null, null);
                    creditsShown = true;
                }
            }
        }

        #endregion

        #region Private methods

        void Update(float speedThisFrame)
        {
            float speedKph = Utils.MsToKmh(speedThisFrame); // convert from m/s to km/h
            float distanceLastFrame = speedThisFrame * Game.LastFrameTime / 1000; // increment odometer counter
            distanceKm += distanceLastFrame;

            if (useMph)
            {
                float speedMph = Utils.KmToMiles(speedKph);
                float distanceMiles = Utils.KmToMiles(distanceKm);
                speedText.Caption = Math.Floor(speedMph).ToString("0 " + mphText); // floor speed mph
                if (speedoMode == SpeedoMode.Detailed)
                {
                    double truncated = Math.Floor(distanceMiles * 10) / 10.0;
                    odometerText.Caption = truncated.ToString("0.0") + " mi";
                }
            }
            else
            {
                speedText.Caption = Math.Floor(speedKph).ToString("0 " + kphText); // floor speed km/h
                if (speedoMode == SpeedoMode.Detailed)
                {
                    double truncated = Math.Floor(distanceKm * 10) / 10.0;
                    odometerText.Caption = truncated.ToString("0.0 km");
                }
            }
        }

        void Draw()
        {
            if (speedoMode != SpeedoMode.Off) speedContainer.Draw();
            if (speedoMode == SpeedoMode.Detailed) // draw these widgets in detailed mode only
                odometerContainer.Draw();
        }

        void ParseSettings()
        {
            try
            {
                settings = ScriptSettings.Load(@".\scripts\Metric_Speedometer.ini");

                // Parse Core settings
                this.useMph = settings.GetValue("Core", "UseMph", false);
                this.speedoMode = (SpeedoMode)settings.GetValue<int>("Core", "DisplayMode", 1);
                this.enableMenu = settings.GetValue("Core", "EnableMenu", true);
                if (enableMenu)
                    this.menuKey = (Keys)Enum.Parse(typeof(Keys), settings.GetValue("Core", "MenuKey"), true);
                this.enableSaving = settings.GetValue("Core", "EnableSaving", true);
                this.rainbowMode = settings.GetValue("Core", "RainbowMode", 0);
				this.onfootSpeedo = settings.GetValue("Core", "OnfootSpeedo", false);

                // Parse UI settings
                this.vAlign = (VerticalAlignment)Enum.Parse(typeof(VerticalAlignment), settings.GetValue("UI", "VertAlign"), true);
                this.hAlign = (HorizontalAlign)Enum.Parse(typeof(HorizontalAlign), settings.GetValue("UI", "HorzAlign"), true);
                this.posOffset = new Point(settings.GetValue<int>("UI", "OffsetX", 0), settings.GetValue<int>("UI", "OffsetY", 0));
                this.pWidth = settings.GetValue("UI", "PanelWidth", 66);
                this.pHeight = settings.GetValue("UI", "PanelHeight", 24);
                this.fontSize = float.Parse(settings.GetValue("UI", "FontSize"), CultureInfo.InvariantCulture.NumberFormat);
                this.fontStyle = settings.GetValue("UI", "FontStyle", 4);
                this.backcolor = Color.FromArgb(settings.GetValue<int>("UI", "BackcolorA", 200), settings.GetValue<int>("UI", "BackcolorR", 237),
                    settings.GetValue<int>("UI", "BackcolorG", 239), settings.GetValue<int>("UI", "BackcolorB", 241));
                this.forecolor = Color.FromArgb(settings.GetValue<int>("UI", "ForecolorA", 255), settings.GetValue<int>("UI", "ForecolorR", 0),
                    settings.GetValue<int>("UI", "ForecolorG", 0), settings.GetValue<int>("UI", "ForecolorB", 0));
                this.kphText = settings.GetValue("Text", "KphText", "km/h");
                this.mphText = settings.GetValue("Text", "MphText", "mph");

                // Load stats
                if (enableSaving) LoadStats();
            }
            catch { UI.Notify("~r~failed to load speedometer config"); }
        }

        void SetupUIElements()
        {
            Point pos = new Point(posOffset.X, posOffset.Y);
            Point odometerPos = new Point(0, 0);
            switch (vAlign)
            {
                case VerticalAlignment.Top:
                    pos.Y += 0;
                    odometerPos.Y += pHeight; // below speed counter
                    break;
                case VerticalAlignment.Center:
                    pos.Y += UI.HEIGHT / 2 - pHeight / 2;
                    odometerPos.Y += pHeight; // below speed counter
                    break;
                case VerticalAlignment.Bottom:
                    pos.Y += UI.HEIGHT - pHeight;
                    odometerPos.Y -= pHeight; // above speed counter
                    break;
            }
            switch (hAlign)
            {
                case HorizontalAlign.Left:
                    pos.X += 0;
                    break;
                case HorizontalAlign.Center:
                    pos.X += UI.WIDTH / 2 - pWidth / 2;
                    break;
                case HorizontalAlign.Right:
                    pos.X += UI.WIDTH - pWidth;
                    break;
            }
            odometerPos.X += pos.X;
            odometerPos.Y += pos.Y;

            this.speedContainer = new UIContainer(pos, new Size(pWidth, pHeight), backcolor);
            this.speedText = new UIText(String.Empty, new Point(pWidth / 2, 0), fontSize, forecolor, (GTA.Font)fontStyle, true);
            this.speedContainer.Items.Add(speedText);
            this.odometerContainer = new UIContainer(odometerPos, new Size(pWidth, pHeight), backcolor);
            this.odometerText = new UIText(String.Empty, new Point(pWidth / 2, 0), fontSize, forecolor, (GTA.Font)fontStyle, true);
            this.odometerContainer.Items.Add(odometerText);
        }

        void ResetUIToDefault()
        {
            vAlign = VerticalAlignment.Bottom;
            hAlign = HorizontalAlign.Center;
            posOffset = new Point(0, 0);
            pWidth = 66;
            pHeight = 24;
            fontSize = 0.5f;
            fontStyle = 4;
            backcolor = Color.FromArgb(150, 237, 239, 241);
            forecolor = Color.FromArgb(255, 0, 0, 0);
            SetupUIElements();
        }

        void SetupMenus()
        {
            // Create main menu
            UIMenuListItem btnToggle = new UIMenuListItem("Toggle Display", new List<dynamic>(Enum.GetNames(typeof(SpeedoMode))), 0);
            btnToggle.OnListChanged += new ItemListEvent(delegate(UIMenuListItem item, int index) {
                speedoMode = (SpeedoMode)(((int)index) % Enum.GetNames(typeof(SpeedoMode)).Length);
            });
            UIMenuItem btnClear = new UIMenuItem("Reset Trip Meter");
            btnClear.Activated += delegate { distanceKm = 0; UI.Notify("Trip meter reset"); };
            UIMenuItem btnCore = new UIMenuItem("Core Settings");
            btnCore.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            UIMenuItem btnDisp = new UIMenuItem("Display Settings");
            btnDisp.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            UIMenuItem btnExtras = new UIMenuItem("Extras");
            btnExtras.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            UIMenuItem btnReload = new UIMenuItem("Reload");
            btnReload.Activated += delegate
            {
                ParseSettings(); SetupUIElements();
                UpdateAllMenuButtons();
                UI.Notify("Speedometer reloaded");
            };
            UIMenuItem btnBack = new UIMenuItem("Save & Close");
            btnBack.Activated += delegate { SaveSettings(); mainMenu.Visible = false; };

            this.mainMenu = new UIMenu(GetTitle(), "by libertylocked");
            foreach (UIMenuItem item in new UIMenuItem[] { btnToggle, btnClear, btnCore, btnDisp, btnExtras, btnReload, btnBack })
            {
                this.mainMenu.AddItem(item);
            }
            this.mainMenu.OnMenuClose += delegate { SaveSettings(); };

            // Create core menu
            UIMenuListItem btnUseMph = new UIMenuListItem("Speed Unit", new List<dynamic> { "Imperial", "Metric" }, 0);
            btnUseMph.OnListChanged += new ItemListEvent(delegate(UIMenuListItem item, int index)
            {
                useMph = index % 2 == 0; UpdateAllMenuButtons();;
            });
            UIMenuCheckboxItem btnEnableSaving = new UIMenuCheckboxItem("Save Trip Meter", false);
            btnEnableSaving.CheckboxEvent += new ItemCheckboxEvent(delegate(UIMenuCheckboxItem item, bool selected)
            {
                enableSaving = selected;
            });
            UIMenuCheckboxItem btnOnfootSpeedo = new UIMenuCheckboxItem("Onfoot Speed", false);
            btnOnfootSpeedo.CheckboxEvent += new ItemCheckboxEvent(delegate(UIMenuCheckboxItem item, bool selected)
            {
                onfootSpeedo = selected;
            });

            this.coreMenu = new UIMenu(GetTitle(), "Core Settings");
            foreach (UIMenuItem item in new UIMenuItem[] { btnUseMph, btnEnableSaving, btnOnfootSpeedo })
            {
                coreMenu.AddItem(item);
            }
            mainMenu.BindMenuToItem(coreMenu, btnCore);

            //// Create display menu
            //MenuButton btnVAlign = new MenuButton("");
            //btnVAlign.Activated += delegate { vAlign = (VerticalAlignment)(((int)vAlign + 1) % 3); posOffset.Y = 0; SetupUIElements(); UpdateDispButtons(0); };
            //MenuButton btnHAlign = new MenuButton("");
            //btnHAlign.Activated += delegate { hAlign = (HorizontalAlign)(((int)hAlign + 1) % 3); posOffset.X = 0; SetupUIElements(); UpdateDispButtons(1); };
            //MenuButton btnFontStyle = new MenuButton("");
            //btnFontStyle.Activated += delegate
            //{
            //    GTA.Font[] fonts = (GTA.Font[])Enum.GetValues(typeof(GTA.Font));
            //    int currIndex = Array.IndexOf(fonts, (GTA.Font)fontStyle);
            //    int nextIndex = (int)fonts[(currIndex + 1) % fonts.Length];
            //    fontStyle = nextIndex; SetupUIElements(); UpdateDispButtons(2);
            //};
            //MenuButton btnFontSize = new MenuButton("Font Size >");
            //btnFontSize.Activated += delegate
            //{
            //    MenuButton btnAddSize = new MenuButton("+ Font Size");
            //    btnAddSize.Activated += delegate { fontSize += 0.02f; SetupUIElements(); };
            //    MenuButton btnSubSize = new MenuButton("- Font Size");
            //    btnSubSize.Activated += delegate { fontSize -= 0.02f; SetupUIElements(); };
            //    GTA.Menu sizeMenu = new GTA.Menu("Font Size", new GTA.IMenuItem[] { btnAddSize, btnSubSize });
            //    sizeMenu.HasFooter = false;
            //    View.AddMenu(sizeMenu);
            //};
            //MenuButton btnPanelSize = new MenuButton("Panel Size >");
            //btnPanelSize.Activated += delegate
            //{
            //    MenuButton btnAddWidth = new MenuButton("+ Panel Width");
            //    btnAddWidth.Activated += delegate { pWidth += 2; SetupUIElements(); };
            //    MenuButton btnSubWidth = new MenuButton("- Panel Width");
            //    btnSubWidth.Activated += delegate { pWidth -= 2; SetupUIElements(); };
            //    MenuButton btnAddHeight = new MenuButton("+ Panel Height");
            //    btnAddHeight.Activated += delegate { pHeight += 2; SetupUIElements(); };
            //    MenuButton btnSubHeight = new MenuButton("- Panel Height");
            //    btnSubHeight.Activated += delegate { pHeight -= 2; SetupUIElements(); };
            //    GTA.Menu panelSizeMenu = new GTA.Menu("Panel Size", new GTA.IMenuItem[] { btnAddWidth, btnSubWidth, btnAddHeight, btnSubHeight });
            //    panelSizeMenu.HasFooter = false;
            //    View.AddMenu(panelSizeMenu);
            //};
            //MenuButton btnAplyOffset = new MenuButton("Set Offset >");
            //btnAplyOffset.Activated += delegate
            //{
            //    MenuButton btnOffsetUp = new MenuButton("Move Up");
            //    btnOffsetUp.Activated += delegate { posOffset.Y += -2; SetupUIElements(); };
            //    MenuButton btnOffsetDown = new MenuButton("Move Down");
            //    btnOffsetDown.Activated += delegate { posOffset.Y += 2; SetupUIElements(); };
            //    MenuButton btnOffsetLeft = new MenuButton("Move Left");
            //    btnOffsetLeft.Activated += delegate { posOffset.X += -2; SetupUIElements(); };
            //    MenuButton btnOffsetRight = new MenuButton("Move Right");
            //    btnOffsetRight.Activated += delegate { posOffset.X += 2; SetupUIElements(); };
            //    MenuButton btnOffsetClr = new MenuButton("Clear Offset");
            //    btnOffsetClr.Activated += delegate { posOffset.X = 0; posOffset.Y = 0; SetupUIElements(); };
            //    GTA.Menu offsetMenu = new GTA.Menu("Set Offset", new GTA.IMenuItem[] { btnOffsetUp, btnOffsetDown, btnOffsetLeft, btnOffsetRight, btnOffsetClr });
            //    offsetMenu.HasFooter = false;
            //    View.AddMenu(offsetMenu);
            //};
            //MenuButton btnBackcolor = new MenuButton("Back Color >");
            //btnBackcolor.Activated += delegate { isChangingBackcolor = true; View.AddMenu(colorMenu); UpdateColorButtons(0); };
            //MenuButton btnForecolor = new MenuButton("Fore Color >");
            //btnForecolor.Activated += delegate { isChangingBackcolor = false; View.AddMenu(colorMenu); UpdateColorButtons(0); };
            //MenuButton btnRstDefault = new MenuButton("Restore to Default");
            //btnRstDefault.Activated += delegate { ResetUIToDefault(); UpdateDispButtons(8); };
            //this.dispMenuItems = new GTA.IMenuItem[] { btnVAlign, btnHAlign, btnFontStyle, btnAplyOffset, btnFontSize, btnPanelSize, btnBackcolor, btnForecolor, btnRstDefault };
            //this.dispMenu = new GTA.Menu("Display Settings", dispMenuItems);
            //this.dispMenu.HasFooter = false;

            //// Create color menu
            //MenuButton btnAddR = new MenuButton("+ R");
            //btnAddR.Activated += delegate 
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 5, 0, 0);
            //    else forecolor = Utils.IncrementARGB(forecolor, 0, 5, 0, 0);
            //    SetupUIElements(); UpdateColorButtons(0);
            //};
            //MenuButton btnSubR = new MenuButton("- R");
            //btnSubR.Activated += delegate
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, -5, 0, 0);
            //    else forecolor = Utils.IncrementARGB(forecolor, 0, -5, 0, 0);
            //    SetupUIElements(); UpdateColorButtons(1);
            //};
            //MenuButton btnAddG = new MenuButton("+ G");
            //btnAddG.Activated += delegate
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, 5, 0);
            //    else forecolor = Utils.IncrementARGB(forecolor, 0, 0, 5, 0);
            //    SetupUIElements(); UpdateColorButtons(2);
            //};
            //MenuButton btnSubG = new MenuButton("- G");
            //btnSubG.Activated += delegate
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, -5, 0);
            //    else forecolor = Utils.IncrementARGB(forecolor, 0, 0, -5, 0);
            //    SetupUIElements(); UpdateColorButtons(3);
            //};
            //MenuButton btnAddB = new MenuButton("+ B");
            //btnAddB.Activated += delegate
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, 0, 5);
            //    else forecolor = Utils.IncrementARGB(forecolor, 0, 0, 0, 5);
            //    SetupUIElements(); UpdateColorButtons(4);
            //};
            //MenuButton btnSubB = new MenuButton("- B");
            //btnSubB.Activated += delegate
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, 0, -5);
            //    else forecolor = Utils.IncrementARGB(forecolor, 0, 0, 0, -5);
            //    SetupUIElements(); UpdateColorButtons(5);
            //};
            //MenuButton btnAddA = new MenuButton("+ Opacity");
            //btnAddA.Activated += delegate
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 5, 0, 0, 0);
            //    else forecolor = Utils.IncrementARGB(forecolor, 5, 0, 0, 0);
            //    SetupUIElements(); UpdateColorButtons(6);
            //};
            //MenuButton btnSubA = new MenuButton("- Opacity");
            //btnSubA.Activated += delegate
            //{
            //    if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, -5, 0, 0, 0);
            //    else forecolor = Utils.IncrementARGB(forecolor, -5, 0, 0, 0);
            //    SetupUIElements(); UpdateColorButtons(7);
            //};
            //this.colorMenuItems = new GTA.IMenuItem[] { btnAddR, btnSubR, btnAddG, btnSubG, btnAddB, btnSubB, btnAddA, btnSubA };
            //this.colorMenu = new GTA.Menu("", colorMenuItems);
            //this.colorMenu.HasFooter = false;
            //this.colorMenu.HeaderHeight += 20;

            //// Create extras menu
            //MenuButton btnRainbowMode = new MenuButton("");
            //btnRainbowMode.Activated += delegate { rainbowMode = (rainbowMode + 1) % 8; if (rainbowMode == 0) SetupUIElements(); UpdateExtrasButtons(0); };
            //MenuButton btnAccTimer = new MenuButton("0-100kph Timer");
            //btnAccTimer.Activated += delegate { wid_accTimer.Toggle(); };
            //MenuButton btnMaxSpeed = new MenuButton("Top Speed Recorder");
            //btnMaxSpeed.Activated += delegate { wid_maxSpeed.Toggle(); };
            //MenuButton btnShowCredits = new MenuButton("Show Credits");
            //btnShowCredits.Activated += ShowCredits;
            //MenuButton btnUpdates = new MenuButton("Check for Updates");
            //btnUpdates.Activated += CheckForUpdates;
            //this.extrasMenuItems = new GTA.IMenuItem[] { btnRainbowMode, btnAccTimer, btnMaxSpeed, btnShowCredits, btnUpdates };
            //this.extrasMenu = new GTA.Menu("Extras", extrasMenuItems);
            //this.extrasMenu.HasFooter = false;

            this.menuPool = new MenuPool();
            menuPool.Add(mainMenu);
            menuPool.Add(coreMenu);
        }

        void UpdateAllMenuButtons()
        {
            ((UIMenuListItem)mainMenu.MenuItems[0]).Index = (int)speedoMode;
            ((UIMenuListItem)coreMenu.MenuItems[0]).Index = useMph ? 0 : 1;
            ((UIMenuCheckboxItem)coreMenu.MenuItems[1]).Checked = enableSaving;
            ((UIMenuCheckboxItem)coreMenu.MenuItems[2]).Checked = onfootSpeedo;
        }

        //void UpdateDispButtons()
        //{
        //    dispMenuItems[0].Caption = "Vertical: " + System.Enum.GetName(typeof(VerticalAlignment), vAlign);
        //    dispMenuItems[1].Caption = "Horizontal: " + System.Enum.GetName(typeof(HorizontalAlign), hAlign);
        //    dispMenuItems[2].Caption = "Font Style: " + fontStyle;
        //}

        //void UpdateColorButtons()
        //{
        //    Color color = isChangingBackcolor ? backcolor : forecolor;
        //    colorMenu.Caption = (isChangingBackcolor ? "Back Color" : "Fore Color") 
        //        + "\nR: " + color.R + " G: " + color.G + " B: " + color.B + " A: " + color.A;
        //}

        //void UpdateExtrasButtons()
        //{
        //    extrasMenuItems[0].Caption = "Rainbow Mode: " + (rainbowMode == 0 ? "Off" : Math.Pow(2, rainbowMode - 1) + "x");
        //    extrasMenuItems[1].Caption = (useMph ? "0-62 mph" : "0-100 kph") + " Timer";
        //}

        string GetTitle()
        {
            return "Speedometer v" + SCRIPT_VERSION;
        }

        void LoadStats()
        {
            try
            {
                using (StreamReader sr = new StreamReader(@".\scripts\Metric_Speedometer_Stats.txt"))
                {
                    bool distanceParsed = float.TryParse(sr.ReadLine(), out distanceKm);
                }
            }
            catch { }
        }

        void SaveStats()
        {
            try
            {
                Thread thread = new Thread(ThreadProc_DoSaveStats);
                thread.Start();
            }
            catch { }
        }

        void ThreadProc_DoSaveStats()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter((@".\scripts\Metric_Speedometer_Stats.txt"), false))
                {
                    sw.WriteLine(distanceKm);
                }
            }
            catch { }
        }

        void ShowCredits(object sender, EventArgs e)
        {
            UI.Notify("Speedometer ~r~v" + SCRIPT_VERSION + " ~s~by ~b~libertylocked");
        }

        void CheckForUpdates(object sender, EventArgs e)
        {
            if (updateCheckState != UpdateCheckState.Stopped) return;
            try
            {
                Thread thread = new Thread(ThreadProc_DoCheckForUpdates);
                thread.Start();
                updateCheckState = UpdateCheckState.Checking;
            }
            catch { updateCheckResult = "~r~failed to check for updates"; }
        }

        void ThreadProc_DoCheckForUpdates()
        {
            try
            {
                WebClient client = new WebClient();
                client.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                Stream stream = client.OpenRead(URL_VERSIONFILE);
                StreamReader reader = new StreamReader(stream);
                string latestVer = reader.ReadToEnd();
                if (SCRIPT_VERSION == latestVer) updateCheckResult = "~g~Speedometer is up to date";
                else updateCheckResult = "~y~New version is available on gta5-mods.com";
            }
            catch { updateCheckResult = "~r~failed to check for updates"; }

            updateCheckState = UpdateCheckState.Checked;
        }

        float GetSpeedFromPosChange(Entity entity)
        {
            float distance = entity.Position.DistanceTo(prevPos);
            return distance / Game.LastFrameTime;
        }

        #endregion

        #region Public methods

        public void SaveSettings()
        {
            try
            {
                INIFile settings = new INIFile(@".\scripts\Metric_Speedometer.ini", true, true);
                settings.SetValue("Core", "UseMph", useMph.ToString());
                settings.SetValue("Core", "DisplayMode", (int)speedoMode);
                settings.SetValue("Core", "EnableSaving", enableSaving.ToString());
                settings.SetValue("Core", "OnfootSpeedo", onfootSpeedo.ToString());
                settings.SetValue("Core", "RainbowMode", rainbowMode);
                settings.SetValue("UI", "VertAlign", Enum.GetName(typeof(VerticalAlignment), vAlign));
                settings.SetValue("UI", "HorzAlign", Enum.GetName(typeof(HorizontalAlign), hAlign));
                settings.SetValue("UI", "OffsetX", posOffset.X);
                settings.SetValue("UI", "OffsetY", posOffset.Y);
                settings.SetValue("UI", "PanelWidth", pWidth);
                settings.SetValue("UI", "PanelHeight", pHeight);
                settings.SetValue("UI", "FontSize", fontSize.ToString(CultureInfo.InvariantCulture));
                settings.SetValue("UI", "FontStyle", fontStyle.ToString());
                settings.SetValue("UI", "BackcolorR", backcolor.R);
                settings.SetValue("UI", "BackcolorG", backcolor.G);
                settings.SetValue("UI", "BackcolorB", backcolor.B);
                settings.SetValue("UI", "BackcolorA", backcolor.A);
                settings.SetValue("UI", "ForecolorR", forecolor.R);
                settings.SetValue("UI", "ForecolorG", forecolor.G);
                settings.SetValue("UI", "ForecolorB", forecolor.B);
                settings.SetValue("UI", "ForecolorA", forecolor.A);

                UI.Notify("Speedometer config saved");
            }
            catch { UI.Notify("~r~failed to save speedometer config"); }
        }

        #endregion
    }

    class MySettingsMenu : GTA.Menu
    {
        Metric_Speedometer script;

        public MySettingsMenu(string caption, GTA.IMenuItem[] items, Metric_Speedometer script)
            : base(caption, items)
        {
            this.script = script;
        }

        public override void OnClose()
        {
            script.SaveSettings();
            base.OnClose();
        }
    }
}
