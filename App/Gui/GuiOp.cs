  //\\   OmenMon: Hardware Monitoring & Control Utility
 //  \\  Copyright © 2023 Piotr Szczepański * License: GPL3
     //  https://omenmon.github.io/

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using OmenMon.Hardware.Bios;
using OmenMon.Hardware.Ec;
using OmenMon.Hardware.Platform;
using OmenMon.Library;

namespace OmenMon.AppGui {

    // Implements a backend for GUI mode operations
    public class GuiOp {

        // Sensors class reference
        internal Platform Platform;

        // Fan program class reference
        internal FanProgram Program;

        // Parent class reference
        private GuiTray Context;

        // Flag to indicate if running on full power
        public bool FullPower { get; private set; }

        // Constructs the operation-running class
        public GuiOp(GuiTray context) {

            // Initialize the parent class reference
            this.Context = context;

            // Initialize the BIOS and the Embedded Controller
            Hw.BiosInit();
            Hw.EcInit();

            // Initialize the hardware platform
            this.Platform = new Platform();

            // Initialize the fan program
            this.Program = new FanProgram(this.Platform, FanProgramCallback);

            // Set the full power flag
            this.FullPower = this.Platform.System.IsFullPower();

        }

        // Shows the about dialog
        public static void About(string title = "", string text = "") {
            (new GuiFormAbout(title, text)).ShowDialog();
        }

        // Automatically applies the configuration on startup
        public void AutoConfig() {

            // Set whether the application should start automatically with Windows
            Hw.TaskSet(Config.TaskId.Gui, Config.AutoStartup);

            // Apply the default GPU power settings
            this.Platform.System.SetGpuPower(
                new BiosData.GpuPowerData(
                    (BiosData.GpuPowerLevel)
                        Enum.Parse(typeof(BiosData.GpuPowerLevel), Config.GpuPowerDefault)));

            // Apply the default fan program,
            // or the alternative program if no AC
            if(this.FullPower)
                // If the default program is set to AutoPerformance, then 
                // enable Performance mode without starting a fan program
                if (Config.FanProgramDefault == "AutoPerformance")
                    this.Platform.Fans.SetMode(BiosData.FanMode.Performance);
                // If the default program is set to AutoDefault, then enable
                // enable Default mode without starting a fan program
                else if (Config.FanProgramDefault == "AutoDefault")
                    this.Platform.Fans.SetMode(BiosData.FanMode.Default);
                // If the default program is set to AutoCool, then enable
                // enable Cool mode without starting a fan program
                else if (Config.FanProgramDefault == "AutoCool")
                    this.Platform.Fans.SetMode(BiosData.FanMode.Cool);
                // In all other cases, start the default fan program
                else
                    this.Program.Run(Config.FanProgramDefault);
            else
                // If the default alt program is set to AutoPerformance, then 
                // enable Performance mode without starting a fan program
                if (Config.FanProgramDefaultAlt == "AutoPerformance")
                    this.Platform.Fans.SetMode(BiosData.FanMode.Performance);
                // If the default alt program is set to AutoDefault, then enable
                // enable Default mode without starting a fan program
                else if (Config.FanProgramDefaultAlt == "AutoDefault")
                    this.Platform.Fans.SetMode(BiosData.FanMode.Default);
                // If the default program is set to AutoCool, then enable
                // enable Cool mode without starting a fan program
                else if (Config.FanProgramDefaultAlt == "AutoCool")
                    this.Platform.Fans.SetMode(BiosData.FanMode.Cool);
                // In all other cases, start the default alt fan program
                else
                    this.Program.Run(Config.FanProgramDefaultAlt, true);

            // Update the main form, if visible
            if(Context.FormMain != null && Context.FormMain.Visible)
                Context.FormMain.UpdateFanCtl();

        }

        // Starts the automatic configuration in another thread
        // so as not to increase the application loading time
        public void AutoConfigRun() {

            Thread autoConfig = new Thread(this.AutoConfig);
            autoConfig.IsBackground = true;
            autoConfig.Start();

        }

        // Keeps updating the status as the fan program runs in the background
        public void FanProgramCallback(FanProgram.Severity severity, string message) {

            // For important status updates only,
            // show a balloon tray notification
            if(severity == FanProgram.Severity.Important)
                Context.ShowBalloonTip(message);

            // Handle notice-severity messages
            else if(severity == FanProgram.Severity.Notice) {

                // Add a prefix if an alternate fan program
                string name = Context.Op.Program.IsAlternate ?
                    Config.Locale.Get(Config.L_PROG + "Alt") + " "
                    + Context.Op.Program.GetName()
                    : Context.Op.Program.GetName();

                // If the main form is available, update the status there
                if(Context.FormMain != null && Context.FormMain.Visible)
                    Context.FormMain.UpdateSysMsg(
                        message.Replace(
                            Config.Locale.Get(Config.L_PROG + "SubMax"),
                            Conv.RTF_SUB1 + Config.Locale.Get(Config.L_PROG + "SubMax") + Conv.RTF_SUBSUP0)
                        + " " + name);

                // Also put it in the tray icon tooltip
                Context.SetNotifyText(
                    Config.Locale.Get(Config.L_PROG) + ": " + name
                    + " @ " + DateTime.Now.ToString(Config.TimestampFormat)
                    + Environment.NewLine + message);

            }

            // Note: Verbose messages are silently ignored in the GUI mode
            // Run OmenMon -Prog <Name> from the command line to see them

        }

        // Launches when the Omen key has been pressed
        public void KeyHandler(Gui.MessageParam lastParam) {

            // If Omen key action is set to custom
            if(Config.KeyCustomActionEnabled) {

                // Launch the action
                Process customAction = new Process();
                customAction.StartInfo.FileName = Config.KeyCustomActionExecCmd;
                customAction.StartInfo.Arguments = Config.KeyCustomActionExecArgs;
                customAction.StartInfo.UseShellExecute = false; // Required for environment change
                customAction.StartInfo.WindowStyle = Config.KeyCustomActionMinimized ?
                    ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal;
                customAction.Start();

            // If Omen key is set to toggle the default fan program 
            // (on subsequent presses, when the form is already shown)
            } else if(Config.KeyToggleFanProgram) {

                // Show the form on first press
                if(Context.FormMain == null || !Context.FormMain.Visible)
                    Context.ShowFormMain();

                else {

                    // Terminate a program, if there is one running
                    if(this.Program.IsEnabled)
                        this.Program.Terminate();

                    // Run the default program, if no program running
                    else
                        this.Program.Run(Config.FanProgramDefault);

                    // Update the main form fan controls
                    // (main form is being shown)
                    Context.FormMain.UpdateFanCtl();

                }

            } else {

                // Just toggle the main form
                Context.ToggleFormMain();

            }

        }

        // Responds to power-mode status change events
        public void PowerChange() {

            // Only if a fan program is active, if configured to do so,
            // and if the power state actually changed from the last-recorded
            if(Config.AutoConfig && this.Program.IsEnabled
                && this.FullPower != this.Platform.System.IsFullPower()) {

                // Toggle the power state
                this.FullPower = !this.FullPower;

                // Apply the default fan program,
                // or the alternative program if no AC
                if(this.FullPower)
                    this.Program.Run(Config.FanProgramDefault);
                else
                    this.Program.Run(Config.FanProgramDefaultAlt, true);

            }

            // Separately also update the main form, if it's visible
            if(Context.FormMain != null && Context.FormMain.Visible)
               Context.FormMain.UpdateSys();

        }

    }

}
