/*
 * Simple Metric/Imperial Speedometer
 * Author: libertylocked
 * Version: 2.1.1
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

namespace GTAVMod_Speedometer
{
    public class Metric_Speedometer : Script
    {
        // Constants
        public const string SCRIPT_VERSION = "2.1.1";
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

        // Fields for menus
        MySettingsMenu mainMenu;
        GTA.Menu coreMenu, dispMenu, colorMenu, extrasMenu;
        GTA.IMenuItem[] mainMenuItems, coreMenuItems, dispMenuItems, colorMenuItems, extrasMenuItems;
        bool isChangingBackcolor;

        // Fields for UI settings
        VerticalAlignment vAlign;
        HorizontalAlign hAlign;
        Point posOffset;
        int pWidth, pHeight;
        float fontSize;
        int fontStyle;
        Color backcolor, forecolor;

        #endregion

        public Metric_Speedometer()
        {
            ParseSettings();
            SetupUIElements();
            SetupMenus();

            UpKey = Keys.NumPad8;
            DownKey = Keys.NumPad2;
            LeftKey = Keys.NumPad4;
            RightKey = Keys.NumPad6;
            ActivateKey = Keys.NumPad5;
            BackKey = Keys.NumPad0;

            this.View.MenuTransitions = false; // because transition looksnice/doesnotlooknice
            this.Tick += OnTick;
            this.KeyDown += OnKeyDown;

            this.wid_accTimer = new AccelerationTimerWidget();
            this.wid_maxSpeed = new MaxSpeedWidget();
        }

        #region Event handles

        void OnTick(object sender, EventArgs e)
        {
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
                if (player.Character.IsInVehicle() || IsPlayerRidingDeer(player.Character))  // conditions to draw speedo
                {
                    // in veh or riding deer
                    float speed = 0;
                    if (player.Character.IsInVehicle()) speed = player.Character.CurrentVehicle.Speed;
                    else if (IsPlayerRidingDeer(player.Character)) speed = GetSpeedFromPosChange(player.Character);

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
                            speedText.Color = HSLA2RGBA((double)rainbowHueBp / 10000, 1, 0.5, forecolor.A / 255.0);
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
                this.View.CloseAllMenus();
                this.View.AddMenu(mainMenu);
                UpdateMainButtons(0);
                
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
            float speedKph = MsToKmh(speedThisFrame); // convert from m/s to km/h
            float distanceLastFrame = speedThisFrame * Game.LastFrameTime / 1000; // increment odometer counter
            distanceKm += distanceLastFrame;

            if (useMph)
            {
                float speedMph = KmToMiles(speedKph);
                float distanceMiles = KmToMiles(distanceKm);
                speedText.Caption = Math.Floor(speedMph).ToString("0 mph"); // floor speed mph
                if (speedoMode == SpeedoMode.Detailed)
                {
                    double truncated = Math.Floor(distanceMiles * 10) / 10.0;
                    odometerText.Caption = truncated.ToString("0.0") + " mi";
                }
            }
            else
            {
                speedText.Caption = Math.Floor(speedKph).ToString("0 km/h"); // floor speed km/h
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
            MenuButton btnToggle = new MenuButton("");
            btnToggle.Activated += delegate { speedoMode = (SpeedoMode)(((int)speedoMode + 1) % Enum.GetNames(typeof(SpeedoMode)).Length); UpdateMainButtons(0); };
            MenuButton btnClear = new MenuButton("Reset Odometer");
            btnClear.Activated += delegate { distanceKm = 0; UI.Notify("Odometer reset"); };
            MenuButton btnCore = new MenuButton("Core Settings >");
            btnCore.Activated += delegate { View.AddMenu(coreMenu); UpdateCoreButtons(0); };
            MenuButton btnDisp = new MenuButton("Display Settings >");
            btnDisp.Activated += delegate { View.AddMenu(dispMenu); UpdateDispButtons(0); };
            MenuButton btnExtras = new MenuButton("Extras >");
            btnExtras.Activated += delegate { View.AddMenu(extrasMenu); UpdateExtrasButtons(0); };
            MenuButton btnReload = new MenuButton("Reload");
            btnReload.Activated += delegate
            {
                ParseSettings(); SetupUIElements();
                UpdateMainButtons(5);
                UI.Notify("Speedometer reloaded");
            };
            MenuButton btnBack = new MenuButton("Save & Exit");
            btnBack.Activated += delegate { View.CloseAllMenus(); };
            this.mainMenuItems = new GTA.IMenuItem[] { btnToggle, btnClear, btnCore, btnDisp, btnExtras, btnReload, btnBack };
            this.mainMenu = new MySettingsMenu("Speedometer v" + SCRIPT_VERSION, mainMenuItems, this);
            this.mainMenu.HasFooter = false;

            // Create core menu
            MenuButton btnUseMph = new MenuButton("");
            btnUseMph.Activated += delegate { useMph = !useMph; UpdateCoreButtons(0); UpdateExtrasButtons(0); };
            MenuButton btnEnableSaving = new MenuButton("");
            btnEnableSaving.Activated += delegate { enableSaving = !enableSaving; UpdateCoreButtons(1); };
            //MenuButton btnEnableMenu = new MenuButton("Disable Menu Key", delegate { enableMenu = !enableMenu; UpdateCoreButtons(2); });
            this.coreMenuItems = new GTA.IMenuItem[] { btnUseMph, btnEnableSaving };
            this.coreMenu = new GTA.Menu("Core Settings", coreMenuItems);
            this.coreMenu.HasFooter = false;

            // Create display menu
            MenuButton btnVAlign = new MenuButton("");
            btnVAlign.Activated += delegate { vAlign = (VerticalAlignment)(((int)vAlign + 1) % 3); posOffset.Y = 0; SetupUIElements(); UpdateDispButtons(0); };
            MenuButton btnHAlign = new MenuButton("");
            btnHAlign.Activated += delegate { hAlign = (HorizontalAlign)(((int)hAlign + 1) % 3); posOffset.X = 0; SetupUIElements(); UpdateDispButtons(1); };
            MenuButton btnFontStyle = new MenuButton("");
            btnFontStyle.Activated += delegate
            {
                GTA.Font[] fonts = (GTA.Font[])Enum.GetValues(typeof(GTA.Font));
                int currIndex = Array.IndexOf(fonts, (GTA.Font)fontStyle);
                int nextIndex = (int)fonts[(currIndex + 1) % fonts.Length];
                fontStyle = nextIndex; SetupUIElements(); UpdateDispButtons(2);
            };
            MenuButton btnFontSize = new MenuButton("Font Size >");
            btnFontSize.Activated += delegate
            {
                MenuButton btnAddSize = new MenuButton("+ Font Size");
                btnAddSize.Activated += delegate { fontSize += 0.02f; SetupUIElements(); };
                MenuButton btnSubSize = new MenuButton("- Font Size");
                btnSubSize.Activated += delegate { fontSize -= 0.02f; SetupUIElements(); };
                GTA.Menu sizeMenu = new GTA.Menu("Font Size", new GTA.IMenuItem[] { btnAddSize, btnSubSize });
                sizeMenu.HasFooter = false;
                View.AddMenu(sizeMenu);
            };
            MenuButton btnPanelSize = new MenuButton("Panel Size >");
            btnPanelSize.Activated += delegate
            {
                MenuButton btnAddWidth = new MenuButton("+ Panel Width");
                btnAddWidth.Activated += delegate { pWidth += 2; SetupUIElements(); };
                MenuButton btnSubWidth = new MenuButton("- Panel Width");
                btnSubWidth.Activated += delegate { pWidth -= 2; SetupUIElements(); };
                MenuButton btnAddHeight = new MenuButton("+ Panel Height");
                btnAddHeight.Activated += delegate { pHeight += 2; SetupUIElements(); };
                MenuButton btnSubHeight = new MenuButton("- Panel Height");
                btnSubHeight.Activated += delegate { pHeight -= 2; SetupUIElements(); };
                GTA.Menu panelSizeMenu = new GTA.Menu("Panel Size", new GTA.IMenuItem[] { btnAddWidth, btnSubWidth, btnAddHeight, btnSubHeight });
                panelSizeMenu.HasFooter = false;
                View.AddMenu(panelSizeMenu);
            };
            MenuButton btnAplyOffset = new MenuButton("Set Offset >");
            btnAplyOffset.Activated += delegate
            {
                MenuButton btnOffsetUp = new MenuButton("Move Up");
                btnOffsetUp.Activated += delegate { posOffset.Y += -2; SetupUIElements(); };
                MenuButton btnOffsetDown = new MenuButton("Move Down");
                btnOffsetDown.Activated += delegate { posOffset.Y += 2; SetupUIElements(); };
                MenuButton btnOffsetLeft = new MenuButton("Move Left");
                btnOffsetLeft.Activated += delegate { posOffset.X += -2; SetupUIElements(); };
                MenuButton btnOffsetRight = new MenuButton("Move Right");
                btnOffsetRight.Activated += delegate { posOffset.X += 2; SetupUIElements(); };
                MenuButton btnOffsetClr = new MenuButton("Clear Offset");
                btnOffsetClr.Activated += delegate { posOffset.X = 0; posOffset.Y = 0; SetupUIElements(); };
                GTA.Menu offsetMenu = new GTA.Menu("Set Offset", new GTA.IMenuItem[] { btnOffsetUp, btnOffsetDown, btnOffsetLeft, btnOffsetRight, btnOffsetClr });
                offsetMenu.HasFooter = false;
                View.AddMenu(offsetMenu);
            };
            MenuButton btnBackcolor = new MenuButton("Back Color >");
            btnBackcolor.Activated += delegate { isChangingBackcolor = true; View.AddMenu(colorMenu); UpdateColorButtons(0); };
            MenuButton btnForecolor = new MenuButton("Fore Color >");
            btnForecolor.Activated += delegate { isChangingBackcolor = false; View.AddMenu(colorMenu); UpdateColorButtons(0); };
            MenuButton btnRstDefault = new MenuButton("Restore to Default");
            btnRstDefault.Activated += delegate { ResetUIToDefault(); UpdateDispButtons(8); };
            this.dispMenuItems = new GTA.IMenuItem[] { btnVAlign, btnHAlign, btnFontStyle, btnAplyOffset, btnFontSize, btnPanelSize, btnBackcolor, btnForecolor, btnRstDefault };
            this.dispMenu = new GTA.Menu("Display Settings", dispMenuItems);
            this.dispMenu.HasFooter = false;

            // Create color menu
            MenuButton btnAddR = new MenuButton("+ R");
            btnAddR.Activated += delegate 
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, 0, 5, 0, 0);
                else forecolor = IncrementARGB(forecolor, 0, 5, 0, 0);
                SetupUIElements(); UpdateColorButtons(0);
            };
            MenuButton btnSubR = new MenuButton("- R");
            btnSubR.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, 0, -5, 0, 0);
                else forecolor = IncrementARGB(forecolor, 0, -5, 0, 0);
                SetupUIElements(); UpdateColorButtons(1);
            };
            MenuButton btnAddG = new MenuButton("+ G");
            btnAddG.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, 0, 0, 5, 0);
                else forecolor = IncrementARGB(forecolor, 0, 0, 5, 0);
                SetupUIElements(); UpdateColorButtons(2);
            };
            MenuButton btnSubG = new MenuButton("- G");
            btnSubG.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, 0, 0, -5, 0);
                else forecolor = IncrementARGB(forecolor, 0, 0, -5, 0);
                SetupUIElements(); UpdateColorButtons(3);
            };
            MenuButton btnAddB = new MenuButton("+ B");
            btnAddB.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, 0, 0, 0, 5);
                else forecolor = IncrementARGB(forecolor, 0, 0, 0, 5);
                SetupUIElements(); UpdateColorButtons(4);
            };
            MenuButton btnSubB = new MenuButton("- B");
            btnSubB.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, 0, 0, 0, -5);
                else forecolor = IncrementARGB(forecolor, 0, 0, 0, -5);
                SetupUIElements(); UpdateColorButtons(5);
            };
            MenuButton btnAddA = new MenuButton("+ Opacity");
            btnAddA.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, 5, 0, 0, 0);
                else forecolor = IncrementARGB(forecolor, 5, 0, 0, 0);
                SetupUIElements(); UpdateColorButtons(6);
            };
            MenuButton btnSubA = new MenuButton("- Opacity");
            btnSubA.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = IncrementARGB(backcolor, -5, 0, 0, 0);
                else forecolor = IncrementARGB(forecolor, -5, 0, 0, 0);
                SetupUIElements(); UpdateColorButtons(7);
            };
            this.colorMenuItems = new GTA.IMenuItem[] { btnAddR, btnSubR, btnAddG, btnSubG, btnAddB, btnSubB, btnAddA, btnSubA };
            this.colorMenu = new GTA.Menu("", colorMenuItems);
            this.colorMenu.HasFooter = false;
            this.colorMenu.HeaderHeight += 20;

            // Create extras menu
            MenuButton btnRainbowMode = new MenuButton("");
            btnRainbowMode.Activated += delegate { rainbowMode = (rainbowMode + 1) % 8; if (rainbowMode == 0) SetupUIElements(); UpdateExtrasButtons(0); };
            MenuButton btnAccTimer = new MenuButton("0-100kph Timer");
            btnAccTimer.Activated += delegate { wid_accTimer.Toggle(); };
            MenuButton btnMaxSpeed = new MenuButton("Max Speed Recorder");
            btnMaxSpeed.Activated += delegate { wid_maxSpeed.Toggle(); };
            MenuButton btnShowCredits = new MenuButton("Show Credits");
            btnShowCredits.Activated += ShowCredits;
            MenuButton btnUpdates = new MenuButton("Check for Updates");
            btnUpdates.Activated += CheckForUpdates;
            this.extrasMenuItems = new GTA.IMenuItem[] { btnRainbowMode, btnAccTimer, btnMaxSpeed, btnShowCredits, btnUpdates };
            this.extrasMenu = new GTA.Menu("Extras", extrasMenuItems);
            this.extrasMenu.HasFooter = false;
        }

        void UpdateMainButtons(int selectedIndex)
        {
            mainMenuItems[0].Caption = "Toggle Display: " + speedoMode.ToString();
            ChangeMenuSelectedIndex(mainMenu, selectedIndex);
        }

        void UpdateCoreButtons(int selectedIndex)
        {
            coreMenuItems[0].Caption = "Speed Unit: " + (useMph ? "Imperial" : "Metric");
            coreMenuItems[1].Caption = "Save Odometer: " + (enableSaving ? "On" : "Off");
            //coreMenuItems[2].Caption = "Enable Menu Key: " + enableMenu;
            ChangeMenuSelectedIndex(coreMenu, selectedIndex);
        }

        void UpdateDispButtons(int selectedIndex)
        {
            dispMenuItems[0].Caption = "Vertical: " + System.Enum.GetName(typeof(VerticalAlignment), vAlign);
            dispMenuItems[1].Caption = "Horizontal: " + System.Enum.GetName(typeof(HorizontalAlign), hAlign);
            dispMenuItems[2].Caption = "Font Style: " + Enum.GetName(typeof(GTA.Font), fontStyle);
            ChangeMenuSelectedIndex(dispMenu, selectedIndex);
        }

        void UpdateColorButtons(int selectedIndex)
        {
            Color color = isChangingBackcolor ? backcolor : forecolor;
            colorMenu.Caption = (isChangingBackcolor ? "Back Color" : "Fore Color") 
                + "\nR: " + color.R + " G: " + color.G + " B: " + color.B + " A: " + color.A;
            ChangeMenuSelectedIndex(colorMenu, selectedIndex);
        }

        void UpdateExtrasButtons(int selectedIndex)
        {
            extrasMenuItems[0].Caption = "Rainbow Mode: " + (rainbowMode == 0 ? "Off" : Math.Pow(2, rainbowMode - 1) + "x");
            extrasMenuItems[1].Caption = (useMph ? "0-62 mph" : "0-100 kph") + " Timer";
            ChangeMenuSelectedIndex(extrasMenu, selectedIndex);
        }

        void ChangeMenuSelectedIndex(GTA.Menu menu, int selectedIndex)
        {
            menu.Initialize();
            for (int i = 0; i < selectedIndex; i++)
                menu.OnChangeSelection(true);
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

        #region Static methods

        public static float MsToKmh(float mPerS)
        {
            return mPerS * 3600 / 1000;
        }

        public static float KmToMiles(float km)
        {
            return km * 0.6213711916666667f;
        }

        public static bool IsPlayerRidingDeer(Ped playerPed)
        {
            try
            {
                Ped attached = Function.Call<Ped>(Hash.GET_ENTITY_ATTACHED_TO, playerPed);
                if (attached != null)
                {
                    PedHash attachedHash = (PedHash)attached.Model.Hash;
                    return (attachedHash == PedHash.Deer);
                }
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        public static Color IncrementARGB(Color color, int dA, int dR, int dG, int dB)
        {
            return Color.FromArgb(Math.Max(Math.Min(color.A + dA, 255), 0), Math.Max(Math.Min(color.R + dR, 255), 0),
                Math.Max(Math.Min(color.G + dG, 255), 0), Math.Max(Math.Min(color.B + dB, 255), 0));
        }

        public static Color HSLA2RGBA(double h, double sl, double l, double a)
        {
            double v;
            double r, g, b;
            r = l;
            g = l;
            b = l;
            v = (l <= 0.5) ? (l * (1.0 + sl)) : (l + sl - l * sl);
            if (v > 0)
            {
                double m;
                double sv;
                int sextant;
                double fract, vsf, mid1, mid2;
                m = l + l - v;
                sv = (v - m) / v;
                h *= 6.0;
                sextant = (int)h;
                fract = h - sextant;
                vsf = v * sv * fract;
                mid1 = m + vsf;
                mid2 = v - vsf;
                switch (sextant)
                {
                    case 0:
                        r = v;
                        g = mid1;
                        b = m;
                        break;
                    case 1:
                        r = mid2;
                        g = v;
                        b = m;
                        break;
                    case 2:
                        r = m;
                        g = v;
                        b = mid1;
                        break;
                    case 3:
                        r = m;
                        g = mid2;
                        b = v;
                        break;
                    case 4:
                        r = mid1;
                        g = m;
                        b = v;
                        break;
                    case 5:
                        r = v;
                        g = m;
                        b = mid2;
                        break;
                }
            }
            int colorR = Math.Min(Convert.ToInt32(r * 255.0f), 255);
            int colorG = Math.Min(Convert.ToInt32(g * 255.0f), 255);
            int colorB = Math.Min(Convert.ToInt32(b * 255.0f), 255);
            int colorA = Math.Min(Convert.ToInt32(a * 255.0f), 255);
            return Color.FromArgb(colorA, colorR, colorG, colorB);
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

    class AccelerationTimerWidget
    {
        public AccelerationTimerState State
        {
            get;
            private set;
        }

        const int TIME_DISPLAYFINISHED = 6; // seconds
        UIText timerText;
        float watchTime;
        float displayTime = 0;

        public AccelerationTimerWidget()
        {
            State = AccelerationTimerState.Off;
            timerText = new UIText("", new Point(UI.WIDTH / 2, UI.HEIGHT / 2), 0.5f, Color.White, 0, true);
        }

        public void Update(float speed)
        {
            switch (State)
            {
                case AccelerationTimerState.Off:
                    break;
                case AccelerationTimerState.WaitingForStop:
                    if (speed == 0)
                    {
                        State = AccelerationTimerState.Ready;
                    }
                    break;
                case AccelerationTimerState.Ready:
                    if (speed != 0)
                    {
                        State = AccelerationTimerState.Counting;
                    }
                    break;
                case AccelerationTimerState.Counting:
                    if (speed >= (float)100000 / 3600) // 100 kph
                    {
                        State = AccelerationTimerState.Finished;
                        timerText.Color = Color.Red;
                        displayTime = 0;
                    }
                    else
                    {
                        watchTime += Game.LastFrameTime;
                    }
                    break;
                case AccelerationTimerState.Finished:
                    displayTime += Game.LastFrameTime;
                    if (displayTime > TIME_DISPLAYFINISHED)
                    {
                        displayTime = 0;
                        this.Stop();
                    }
                    break;
            }
        }

        public void Draw()
        {
            if (State != AccelerationTimerState.Off)
            {
                timerText.Caption = watchTime.ToString("0.000s");
                if (State == AccelerationTimerState.WaitingForStop) timerText.Caption += "\nPlease stop your vehicle";
                else if (State == AccelerationTimerState.Ready) timerText.Caption += "\nReady";
                timerText.Draw();
            }
        }

        public void Toggle()
        {
            if (State == AccelerationTimerState.Off) Start();
            else Stop();
        }

        public void Start()
        {
            State = AccelerationTimerState.WaitingForStop;
            watchTime = 0;
            timerText.Color = Color.White; // reset color
        }

        public void Stop()
        {
            State = AccelerationTimerState.Off;
        }
    }

    class MaxSpeedWidget
    {
        float maxSpeed;
        UIText maxSpeedText;

        public MaxSpeedState State
        {
            get;
            private set;
        }

        public MaxSpeedWidget()
        {
            State = MaxSpeedState.Off;
            maxSpeedText = new UIText("", new Point(UI.WIDTH / 2, UI.HEIGHT / 2 + 50), 0.5f, Color.White, 0, true);
            maxSpeed = 0;
        }

        public void Update(float speed)
        {
            if (State == MaxSpeedState.Counting)
            {
                if (speed > maxSpeed) maxSpeed = speed;
            }
        }

        public void Draw(bool useMph)
        {
            if (State == MaxSpeedState.Counting)
            {
                float speedKph = Metric_Speedometer.MsToKmh(maxSpeed);
                maxSpeedText.Caption = "Max: " + (useMph ? Metric_Speedometer.KmToMiles(speedKph).ToString("0.0 mph") : speedKph.ToString("0.0 km/h"));
                maxSpeedText.Draw();
            }
        }

        public void Toggle()
        {
            if (State == MaxSpeedState.Off) Start();
            else Stop();
        }

        public void Start()
        {
            this.maxSpeed = 0;
            State = MaxSpeedState.Counting;
        }

        public void Stop()
        {
            State = MaxSpeedState.Off;
        }
    }

    #region Enums

    enum SpeedoMode
    {
        Off = 0,
        Simple = 1,
        Detailed = 2,
    }

    enum UpdateCheckState
    {
        Stopped = 0,
        Checking = 1,
        Checked = 2,
    }

    enum AccelerationTimerState
    {
        Off = 0, // off
        WaitingForStop = 1, // veh not stopped
        Ready = 2, // veh stopped, timer ready
        Counting = 3, // veh accelerating, timer counting
        Finished = 4, // veh reached 100kph, timer stops
    }

    enum MaxSpeedState
    {
        Off = 0,
        Counting = 1,
    }

    #endregion

    #region INI File class

    internal class INIFile
    {

        #region "Declarations"

        // *** Lock for thread-safe access to file and local cache ***
        private object m_Lock = new object();

        // *** File name ***
        private string m_FileName = null;
        internal string FileName
        {
            get
            {
                return m_FileName;
            }
        }

        // *** Lazy loading flag ***
        private bool m_Lazy = false;

        // *** Automatic flushing flag ***
        private bool m_AutoFlush = false;

        // *** Local cache ***
        private Dictionary<string, Dictionary<string, string>> m_Sections = new Dictionary<string, Dictionary<string, string>>();
        private Dictionary<string, Dictionary<string, string>> m_Modified = new Dictionary<string, Dictionary<string, string>>();

        // *** Local cache modified flag ***
        private bool m_CacheModified = false;

        #endregion

        #region "Methods"

        // *** Constructor ***
        public INIFile(string FileName)
        {
            Initialize(FileName, false, false);
        }

        public INIFile(string FileName, bool Lazy, bool AutoFlush)
        {
            Initialize(FileName, Lazy, AutoFlush);
        }

        // *** Initialization ***
        private void Initialize(string FileName, bool Lazy, bool AutoFlush)
        {
            m_FileName = FileName;
            m_Lazy = Lazy;
            m_AutoFlush = AutoFlush;
            if (!m_Lazy) Refresh();
        }

        // *** Parse section name ***
        private string ParseSectionName(string Line)
        {
            if (!Line.StartsWith("[")) return null;
            if (!Line.EndsWith("]")) return null;
            if (Line.Length < 3) return null;
            return Line.Substring(1, Line.Length - 2);
        }

        // *** Parse key+value pair ***
        private bool ParseKeyValuePair(string Line, ref string Key, ref string Value)
        {
            // *** Check for key+value pair ***
            int i;
            if ((i = Line.IndexOf('=')) <= 0) return false;

            int j = Line.Length - i - 1;
            Key = Line.Substring(0, i).Trim();
            if (Key.Length <= 0) return false;

            Value = (j > 0) ? (Line.Substring(i + 1, j).Trim()) : ("");
            return true;
        }

        // *** Read file contents into local cache ***
        internal void Refresh()
        {
            lock (m_Lock)
            {
                StreamReader sr = null;
                try
                {
                    // *** Clear local cache ***
                    m_Sections.Clear();
                    m_Modified.Clear();

                    // *** Open the INI file ***
                    try
                    {
                        sr = new StreamReader(m_FileName);
                    }
                    catch (FileNotFoundException)
                    {
                        return;
                    }

                    // *** Read up the file content ***
                    Dictionary<string, string> CurrentSection = null;
                    string s;
                    string SectionName;
                    string Key = null;
                    string Value = null;
                    while ((s = sr.ReadLine()) != null)
                    {
                        s = s.Trim();

                        // *** Check for section names ***
                        SectionName = ParseSectionName(s);
                        if (SectionName != null)
                        {
                            // *** Only first occurrence of a section is loaded ***
                            if (m_Sections.ContainsKey(SectionName))
                            {
                                CurrentSection = null;
                            }
                            else
                            {
                                CurrentSection = new Dictionary<string, string>();
                                m_Sections.Add(SectionName, CurrentSection);
                            }
                        }
                        else if (CurrentSection != null)
                        {
                            // *** Check for key+value pair ***
                            if (ParseKeyValuePair(s, ref Key, ref Value))
                            {
                                // *** Only first occurrence of a key is loaded ***
                                if (!CurrentSection.ContainsKey(Key))
                                {
                                    CurrentSection.Add(Key, Value);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    // *** Cleanup: close file ***
                    if (sr != null) sr.Close();
                    sr = null;
                }
            }
        }

        // *** Flush local cache content ***
        internal void Flush()
        {
            lock (m_Lock)
            {
                PerformFlush();
            }
        }

        private void PerformFlush()
        {
            // *** If local cache was not modified, exit ***
            if (!m_CacheModified) return;
            m_CacheModified = false;

            // *** Check if original file exists ***
            bool OriginalFileExists = File.Exists(m_FileName);

            // *** Get temporary file name ***
            string TmpFileName = Path.ChangeExtension(m_FileName, "$n$");

            // *** Copy content of original file to temporary file, replace modified values ***
            StreamWriter sw = null;

            // *** Create the temporary file ***
            sw = new StreamWriter(TmpFileName);

            try
            {
                Dictionary<string, string> CurrentSection = null;
                if (OriginalFileExists)
                {
                    StreamReader sr = null;
                    try
                    {
                        // *** Open the original file ***
                        sr = new StreamReader(m_FileName);

                        // *** Read the file original content, replace changes with local cache values ***
                        string s;
                        string SectionName;
                        string Key = null;
                        string Value = null;
                        bool Unmodified;
                        bool Reading = true;
                        while (Reading)
                        {
                            s = sr.ReadLine();
                            Reading = (s != null);

                            // *** Check for end of file ***
                            if (Reading)
                            {
                                Unmodified = true;
                                s = s.Trim();
                                SectionName = ParseSectionName(s);
                            }
                            else
                            {
                                Unmodified = false;
                                SectionName = null;
                            }

                            // *** Check for section names ***
                            if ((SectionName != null) || (!Reading))
                            {
                                if (CurrentSection != null)
                                {
                                    // *** Write all remaining modified values before leaving a section ****
                                    if (CurrentSection.Count > 0)
                                    {
                                        foreach (string fkey in CurrentSection.Keys)
                                        {
                                            if (CurrentSection.TryGetValue(fkey, out Value))
                                            {
                                                sw.Write(fkey);
                                                sw.Write('=');
                                                sw.WriteLine(Value);
                                            }
                                        }
                                        sw.WriteLine();
                                        CurrentSection.Clear();
                                    }
                                }

                                if (Reading)
                                {
                                    // *** Check if current section is in local modified cache ***
                                    if (!m_Modified.TryGetValue(SectionName, out CurrentSection))
                                    {
                                        CurrentSection = null;
                                    }
                                }
                            }
                            else if (CurrentSection != null)
                            {
                                // *** Check for key+value pair ***
                                if (ParseKeyValuePair(s, ref Key, ref Value))
                                {
                                    if (CurrentSection.TryGetValue(Key, out Value))
                                    {
                                        // *** Write modified value to temporary file ***
                                        Unmodified = false;
                                        CurrentSection.Remove(Key);

                                        sw.Write(Key);
                                        sw.Write('=');
                                        sw.WriteLine(Value);
                                    }
                                }
                            }

                            // *** Write unmodified lines from the original file ***
                            if (Unmodified)
                            {
                                sw.WriteLine(s);
                            }
                        }

                        // *** Close the original file ***
                        sr.Close();
                        sr = null;
                    }
                    finally
                    {
                        // *** Cleanup: close files ***                  
                        if (sr != null) sr.Close();
                        sr = null;
                    }
                }

                // *** Cycle on all remaining modified values ***
                foreach (KeyValuePair<string, Dictionary<string, string>> SectionPair in m_Modified)
                {
                    CurrentSection = SectionPair.Value;
                    if (CurrentSection.Count > 0)
                    {
                        sw.WriteLine();

                        // *** Write the section name ***
                        sw.Write('[');
                        sw.Write(SectionPair.Key);
                        sw.WriteLine(']');

                        // *** Cycle on all key+value pairs in the section ***
                        foreach (KeyValuePair<string, string> ValuePair in CurrentSection)
                        {
                            // *** Write the key+value pair ***
                            sw.Write(ValuePair.Key);
                            sw.Write('=');
                            sw.WriteLine(ValuePair.Value);
                        }
                        CurrentSection.Clear();
                    }
                }
                m_Modified.Clear();

                // *** Close the temporary file ***
                sw.Close();
                sw = null;

                // *** Rename the temporary file ***
                File.Copy(TmpFileName, m_FileName, true);

                // *** Delete the temporary file ***
                File.Delete(TmpFileName);
            }
            finally
            {
                // *** Cleanup: close files ***                  
                if (sw != null) sw.Close();
                sw = null;
            }
        }

        // *** Read a value from local cache ***
        internal string GetValue(string SectionName, string Key, string DefaultValue)
        {
            // *** Lazy loading ***
            if (m_Lazy)
            {
                m_Lazy = false;
                Refresh();
            }

            lock (m_Lock)
            {
                // *** Check if the section exists ***
                Dictionary<string, string> Section;
                if (!m_Sections.TryGetValue(SectionName, out Section)) return DefaultValue;

                // *** Check if the key exists ***
                string Value;
                if (!Section.TryGetValue(Key, out Value)) return DefaultValue;

                // *** Return the found value ***
                return Value;
            }
        }

        // *** Insert or modify a value in local cache ***
        internal void SetValue(string SectionName, string Key, string Value)
        {
            // *** Lazy loading ***
            if (m_Lazy)
            {
                m_Lazy = false;
                Refresh();
            }

            lock (m_Lock)
            {
                // *** Flag local cache modification ***
                m_CacheModified = true;

                // *** Check if the section exists ***
                Dictionary<string, string> Section;
                if (!m_Sections.TryGetValue(SectionName, out Section))
                {
                    // *** If it doesn't, add it ***
                    Section = new Dictionary<string, string>();
                    m_Sections.Add(SectionName, Section);
                }

                // *** Modify the value ***
                if (Section.ContainsKey(Key)) Section.Remove(Key);
                Section.Add(Key, Value);

                // *** Add the modified value to local modified values cache ***
                if (!m_Modified.TryGetValue(SectionName, out Section))
                {
                    Section = new Dictionary<string, string>();
                    m_Modified.Add(SectionName, Section);
                }

                if (Section.ContainsKey(Key)) Section.Remove(Key);
                Section.Add(Key, Value);

                // *** Automatic flushing : immediately write any modification to the file ***
                if (m_AutoFlush) PerformFlush();
            }
        }

        // *** Encode byte array ***
        private string EncodeByteArray(byte[] Value)
        {
            if (Value == null) return null;

            StringBuilder sb = new StringBuilder();
            foreach (byte b in Value)
            {
                string hex = Convert.ToString(b, 16);
                int l = hex.Length;
                if (l > 2)
                {
                    sb.Append(hex.Substring(l - 2, 2));
                }
                else
                {
                    if (l < 2) sb.Append("0");
                    sb.Append(hex);
                }
            }
            return sb.ToString();
        }

        // *** Decode byte array ***
        private byte[] DecodeByteArray(string Value)
        {
            if (Value == null) return null;

            int l = Value.Length;
            if (l < 2) return new byte[] { };

            l /= 2;
            byte[] Result = new byte[l];
            for (int i = 0; i < l; i++) Result[i] = Convert.ToByte(Value.Substring(i * 2, 2), 16);
            return Result;
        }

        // *** Getters for various types ***
        internal bool GetValue(string SectionName, string Key, bool DefaultValue)
        {
            string StringValue = GetValue(SectionName, Key, DefaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
            bool Value;
            if (bool.TryParse(StringValue, out Value)) return (Value);
            return DefaultValue;
        }

        internal int GetValue(string SectionName, string Key, int DefaultValue)
        {
            string StringValue = GetValue(SectionName, Key, DefaultValue.ToString(CultureInfo.InvariantCulture));
            int Value;
            if (int.TryParse(StringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out Value)) return Value;
            return DefaultValue;
        }

        internal long GetValue(string SectionName, string Key, long DefaultValue)
        {
            string StringValue = GetValue(SectionName, Key, DefaultValue.ToString(CultureInfo.InvariantCulture));
            long Value;
            if (long.TryParse(StringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out Value)) return Value;
            return DefaultValue;
        }

        internal double GetValue(string SectionName, string Key, double DefaultValue)
        {
            string StringValue = GetValue(SectionName, Key, DefaultValue.ToString(CultureInfo.InvariantCulture));
            double Value;
            if (double.TryParse(StringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out Value)) return Value;
            return DefaultValue;
        }

        internal byte[] GetValue(string SectionName, string Key, byte[] DefaultValue)
        {
            string StringValue = GetValue(SectionName, Key, EncodeByteArray(DefaultValue));
            try
            {
                return DecodeByteArray(StringValue);
            }
            catch (FormatException)
            {
                return DefaultValue;
            }
        }

        internal DateTime GetValue(string SectionName, string Key, DateTime DefaultValue)
        {
            string StringValue = GetValue(SectionName, Key, DefaultValue.ToString(CultureInfo.InvariantCulture));
            DateTime Value;
            if (DateTime.TryParse(StringValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AssumeLocal, out Value)) return Value;
            return DefaultValue;
        }

        // *** Setters for various types ***
        internal void SetValue(string SectionName, string Key, bool Value)
        {
            SetValue(SectionName, Key, (Value).ToString(CultureInfo.InvariantCulture));
        }

        internal void SetValue(string SectionName, string Key, int Value)
        {
            SetValue(SectionName, Key, Value.ToString(CultureInfo.InvariantCulture));
        }

        internal void SetValue(string SectionName, string Key, long Value)
        {
            SetValue(SectionName, Key, Value.ToString(CultureInfo.InvariantCulture));
        }

        internal void SetValue(string SectionName, string Key, double Value)
        {
            SetValue(SectionName, Key, Value.ToString(CultureInfo.InvariantCulture));
        }

        internal void SetValue(string SectionName, string Key, byte[] Value)
        {
            SetValue(SectionName, Key, EncodeByteArray(Value));
        }

        internal void SetValue(string SectionName, string Key, DateTime Value)
        {
            SetValue(SectionName, Key, Value.ToString(CultureInfo.InvariantCulture));
        }

        #endregion

    }
#endregion
}
