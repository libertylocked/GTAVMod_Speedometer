/*
 * Simple Metric/Imperial Speedometer
 * Author: libertylocked
 * License: GPLv2
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
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
        public const string SCRIPT_VERSION = "2.2.0";
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
                    //ShowCredits();
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
            kphText = "km/h";
            mphText = "mph";
            SetupUIElements();
        }

        void SetupMenus()
        {
            // Create main menu
            UIMenuListItem btnToggle = new UIMenuListItem("Toggle Display", new List<dynamic>(Enum.GetNames(typeof(SpeedoMode))), 0);
            btnToggle.OnListChanged += delegate(UIMenuListItem item, int index) {
                speedoMode = (SpeedoMode)(((int)index) % Enum.GetNames(typeof(SpeedoMode)).Length);
            };
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

            mainMenu = new UIMenu(GetTitle(), "By libertylocked");
            foreach (UIMenuItem item in new UIMenuItem[] { btnToggle, btnClear, btnCore, btnDisp, btnExtras, btnReload, btnBack })
            {
                mainMenu.AddItem(item);
            }
            mainMenu.OnMenuClose += delegate { SaveSettings(); };

            // Create core menu
            UIMenuListItem btnUseMph = new UIMenuListItem("Speed Unit", new List<dynamic> { "Imperial", "Metric" }, 0, "Sets the unit between KPH and MPH");
            btnUseMph.OnListChanged += delegate(UIMenuListItem item, int index)
            {
                useMph = index % 2 == 0; UpdateAllMenuButtons();;
            };
            UIMenuCheckboxItem btnEnableSaving = new UIMenuCheckboxItem("Save Trip Meter", false, "Allows trip meter data to be persistent across game sessions");
            btnEnableSaving.CheckboxEvent += new ItemCheckboxEvent(delegate(UIMenuCheckboxItem item, bool selected)
            {
                enableSaving = selected;
            });
            UIMenuCheckboxItem btnOnfootSpeedo = new UIMenuCheckboxItem("Onfoot Speed", false, "Shows speed when player is on foot");
            btnOnfootSpeedo.CheckboxEvent += new ItemCheckboxEvent(delegate(UIMenuCheckboxItem item, bool selected)
            {
                onfootSpeedo = selected;
            });

            coreMenu = new UIMenu(GetTitle(), "Core Settings");
            foreach (UIMenuItem item in new UIMenuItem[] { btnUseMph, btnEnableSaving, btnOnfootSpeedo })
            {
                coreMenu.AddItem(item);
            }
            mainMenu.BindMenuToItem(coreMenu, btnCore);

            // Create display menu
            UIMenuListItem btnVAlign = new UIMenuListItem("Vertical Alignment", new List<dynamic>(Enum.GetNames(typeof(VerticalAlignment))), 0, "Determines how speedometer display will be aligned vertically");
            btnVAlign.OnListChanged += delegate(UIMenuListItem item, int index)
            {
                vAlign = (VerticalAlignment)(((int)index) % 3); posOffset.Y = 0; SetupUIElements();
            };
            UIMenuListItem btnHAlign = new UIMenuListItem("Horizontal Alignment", new List<dynamic>(Enum.GetNames(typeof(HorizontalAlign))), 0, "Determines how speedometer display will be aligned horizontally");
            btnHAlign.OnListChanged += delegate(UIMenuListItem item, int index)
            {
                hAlign = (HorizontalAlign)(((int)index) % 3); posOffset.X = 0; SetupUIElements();
            };
            UIMenuListItem btnFontStyle = new UIMenuListItem("Font Style", new List<dynamic>(Enum.GetNames(typeof(GTA.Font))), 0, "Sets the font on speedometer display");
            btnFontStyle.OnListChanged += delegate(UIMenuListItem item, int index)
            {
                fontStyle = (int)((GTA.Font[])Enum.GetValues(typeof(GTA.Font)))[index]; SetupUIElements();
            };
            UIMenuItem btnFontSize = new UIMenuItem("Font Size", "Sets the size of text on speedometer");
            btnFontSize.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            UIMenuItem btnPanelSize = new UIMenuItem("Panel Size", "Sets the size of the back rectangle");
            btnPanelSize.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            UIMenuItem btnOffset = new UIMenuItem("Apply Offset", "Applies an offset to speedometer display, to fine tune its position");
            btnOffset.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            UIMenuItem btnBackcolor = new UIMenuItem("Back Color", "Sets the color of the background panel");
            btnBackcolor.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            btnBackcolor.Activated += delegate { isChangingBackcolor = true; UpdateAllMenuButtons(); };
            UIMenuItem btnForecolor = new UIMenuItem("Fore Color", "Sets the color of the text");
            btnForecolor.SetLeftBadge(UIMenuItem.BadgeStyle.Star);
            btnForecolor.Activated += delegate { isChangingBackcolor = false; UpdateAllMenuButtons(); };
            UIMenuItem btnTxt = new UIMenuItem("Speed Unit Text", "Changes the text for speed unit");
            btnTxt.Activated += delegate 
            {
                string input = Game.GetUserInput(WindowTitle.CELL_EMASH_BOD, useMph ? mphText : kphText, 20);
                if (useMph) mphText = input;
                else kphText = input;
            };
            UIMenuItem btnRstDefault = new UIMenuItem("Restore to Default", "Resets UI to default settings");
            btnRstDefault.Activated += delegate { ResetUIToDefault(); UpdateAllMenuButtons(); };

            dispMenu = new UIMenu(GetTitle(), "Display Settings");
            foreach (UIMenuItem item in new UIMenuItem[] { btnVAlign, btnHAlign, btnFontStyle, btnFontSize, btnPanelSize, btnOffset, btnBackcolor, btnForecolor, btnTxt, btnRstDefault })
            {
                dispMenu.AddItem(item);
            }
            mainMenu.BindMenuToItem(dispMenu, btnDisp);

            // Create font size menu
            UIMenuItem btnAddSize = new UIMenuItem("+ Font Size");
            btnAddSize.Activated += delegate { fontSize += 0.02f; SetupUIElements(); };
            UIMenuItem btnSubSize = new UIMenuItem("- Font Size");
            btnSubSize.Activated += delegate { fontSize -= 0.02f; SetupUIElements(); };
            UIMenu sizeMenu = new UIMenu(GetTitle(), "Font Size");
            sizeMenu.AddItem(btnAddSize);
            sizeMenu.AddItem(btnSubSize);
            dispMenu.BindMenuToItem(sizeMenu, btnFontSize);

            // Create panel size menu
            UIMenuItem btnAddWidth = new UIMenuItem("+ Panel Width");
            btnAddWidth.Activated += delegate { pWidth += 2; SetupUIElements(); };
            UIMenuItem btnSubWidth = new UIMenuItem("- Panel Width");
            btnSubWidth.Activated += delegate { pWidth -= 2; SetupUIElements(); };
            UIMenuItem btnAddHeight = new UIMenuItem("+ Panel Height");
            btnAddHeight.Activated += delegate { pHeight += 2; SetupUIElements(); };
            UIMenuItem btnSubHeight = new UIMenuItem("- Panel Height");
            btnSubHeight.Activated += delegate { pHeight -= 2; SetupUIElements(); };
            UIMenu panelSizeMenu = new UIMenu(GetTitle(), "Panel Size");
            panelSizeMenu.AddItem(btnAddWidth);
            panelSizeMenu.AddItem(btnSubWidth);
            panelSizeMenu.AddItem(btnAddHeight);
            panelSizeMenu.AddItem(btnSubHeight);
            dispMenu.BindMenuToItem(panelSizeMenu, btnPanelSize);

            // Create offset menu
            UIMenuItem btnOffsetUp = new UIMenuItem("Move Up");
            btnOffsetUp.Activated += delegate { posOffset.Y += -2; SetupUIElements(); };
            UIMenuItem btnOffsetDown = new UIMenuItem("Move Down");
            btnOffsetDown.Activated += delegate { posOffset.Y += 2; SetupUIElements(); };
            UIMenuItem btnOffsetLeft = new UIMenuItem("Move Left");
            btnOffsetLeft.Activated += delegate { posOffset.X += -2; SetupUIElements(); };
            UIMenuItem btnOffsetRight = new UIMenuItem("Move Right");
            btnOffsetRight.Activated += delegate { posOffset.X += 2; SetupUIElements(); };
            UIMenuItem btnOffsetClr = new UIMenuItem("Clear Offset");
            btnOffsetClr.Activated += delegate { posOffset.X = 0; posOffset.Y = 0; SetupUIElements(); };
            UIMenu offsetMenu = new UIMenu(GetTitle(), "Apply Offset");
            offsetMenu.AddItem(btnOffsetUp);
            offsetMenu.AddItem(btnOffsetDown);
            offsetMenu.AddItem(btnOffsetLeft);
            offsetMenu.AddItem(btnOffsetRight);
            offsetMenu.AddItem(btnOffsetClr);
            dispMenu.BindMenuToItem(offsetMenu, btnOffset);

            // Create color menu
            UIMenuItem btnAddR = new UIMenuItem("+ R");
            btnAddR.Activated += delegate 
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 5, 0, 0);
                else forecolor = Utils.IncrementARGB(forecolor, 0, 5, 0, 0);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            UIMenuItem btnSubR = new UIMenuItem("- R");
            btnSubR.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, -5, 0, 0);
                else forecolor = Utils.IncrementARGB(forecolor, 0, -5, 0, 0);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            UIMenuItem btnAddG = new UIMenuItem("+ G");
            btnAddG.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, 5, 0);
                else forecolor = Utils.IncrementARGB(forecolor, 0, 0, 5, 0);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            UIMenuItem btnSubG = new UIMenuItem("- G");
            btnSubG.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, -5, 0);
                else forecolor = Utils.IncrementARGB(forecolor, 0, 0, -5, 0);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            UIMenuItem btnAddB = new UIMenuItem("+ B");
            btnAddB.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, 0, 5);
                else forecolor = Utils.IncrementARGB(forecolor, 0, 0, 0, 5);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            UIMenuItem btnSubB = new UIMenuItem("- B");
            btnSubB.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 0, 0, 0, -5);
                else forecolor = Utils.IncrementARGB(forecolor, 0, 0, 0, -5);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            UIMenuItem btnAddA = new UIMenuItem("+ Opacity");
            btnAddA.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, 5, 0, 0, 0);
                else forecolor = Utils.IncrementARGB(forecolor, 5, 0, 0, 0);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            UIMenuItem btnSubA = new UIMenuItem("- Opacity");
            btnSubA.Activated += delegate
            {
                if (isChangingBackcolor) backcolor = Utils.IncrementARGB(backcolor, -5, 0, 0, 0);
                else forecolor = Utils.IncrementARGB(forecolor, -5, 0, 0, 0);
                SetupUIElements(); UpdateAllMenuButtons();
            };
            colorMenu = new UIMenu(GetTitle(), "Set Color");
            foreach (UIMenuItem item in new UIMenuItem[] { btnAddR, btnSubR, btnAddG, btnSubG, btnAddB, btnSubB, btnAddA, btnSubA })
            {
                colorMenu.AddItem(item);
            }
            dispMenu.BindMenuToItem(colorMenu, btnBackcolor);
            dispMenu.BindMenuToItem(colorMenu, btnForecolor);

            // Create extras menu
            UIMenuListItem btnRainbowMode = new UIMenuListItem("Rainbow Mode", new List<dynamic> { "Off", "1x", "2x", "4x", "8x", "16x", "32x", "64x" }, 0);
            btnRainbowMode.OnListChanged += delegate(UIMenuListItem item, int index)
            {
                rainbowMode = index;
                SetupUIElements();
            };
            UIMenuItem btnAccTimer = new UIMenuItem("0-100kph/62mph Timer");
            btnAccTimer.Activated += delegate { wid_accTimer.Toggle(); };
            UIMenuItem btnMaxSpeed = new UIMenuItem("Top Speed Recorder");
            btnMaxSpeed.Activated += delegate { wid_maxSpeed.Toggle(); };
            UIMenuItem btnShowCredits = new UIMenuItem("Show Credits");
            btnShowCredits.Activated += delegate { ShowCredits(); };
            UIMenuItem btnUpdates = new UIMenuItem("Check for Updates");
            btnUpdates.Activated += delegate { CheckForUpdates(); };

            extrasMenu = new UIMenu(GetTitle(), "Extras");
            foreach (UIMenuItem item in new UIMenuItem[] { btnRainbowMode, btnAccTimer, btnMaxSpeed, btnShowCredits, btnUpdates })
            {
                extrasMenu.AddItem(item);
            }
            mainMenu.BindMenuToItem(extrasMenu, btnExtras);

            menuPool = new MenuPool();
            menuPool.Add(mainMenu);
            menuPool.Add(coreMenu);
            menuPool.Add(dispMenu);
            menuPool.Add(extrasMenu);
            menuPool.Add(sizeMenu);
            menuPool.Add(panelSizeMenu);
            menuPool.Add(offsetMenu);
            menuPool.Add(colorMenu);
        }

        void UpdateAllMenuButtons()
        {
            ((UIMenuListItem)mainMenu.MenuItems[0]).Index = (int)speedoMode;
            ((UIMenuListItem)coreMenu.MenuItems[0]).Index = useMph ? 0 : 1;
            ((UIMenuCheckboxItem)coreMenu.MenuItems[1]).Checked = enableSaving;
            ((UIMenuCheckboxItem)coreMenu.MenuItems[2]).Checked = onfootSpeedo;
            ((UIMenuListItem)dispMenu.MenuItems[0]).Index = (int)vAlign;
            ((UIMenuListItem)dispMenu.MenuItems[1]).Index = (int)hAlign;
            ((UIMenuListItem)dispMenu.MenuItems[2]).Index = Array.IndexOf(Enum.GetValues(typeof(GTA.Font)), (GTA.Font)fontStyle);

            Color color = isChangingBackcolor ? backcolor : forecolor;
            foreach (UIMenuItem item in colorMenu.MenuItems)
            {
                item.Description = (isChangingBackcolor ? "Back Color" : "Fore Color") + "\nR: " + color.R + " G: " + color.G + " B: " + color.B + " A: " + color.A;
            }

            ((UIMenuListItem)extrasMenu.MenuItems[0]).Index = rainbowMode;
        }

        //void UpdateColorButtons()
        //{
        //    Color color = isChangingBackcolor ? backcolor : forecolor;
        //    colorMenu.Caption = (isChangingBackcolor ? "Back Color" : "Fore Color") 
        //        + "\nR: " + color.R + " G: " + color.G + " B: " + color.B + " A: " + color.A;
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

        void ShowCredits()
        {
            UI.Notify("Speedometer ~r~v" + SCRIPT_VERSION + " ~s~by ~b~libertylocked");
        }

        void CheckForUpdates()
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
                settings.SetValue("Core", "OnfootSpeedo", onfootSpeedo.ToString());
                settings.SetValue("Text", "KphText", kphText);
                settings.SetValue("Text", "MphText", mphText);
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
}
