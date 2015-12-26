using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using GTA;

namespace GTAVMod_Speedometer
{
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
}
