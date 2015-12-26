using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using GTA;

namespace GTAVMod_Speedometer
{
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
                float speedKph = Utils.MsToKmh(maxSpeed);
                maxSpeedText.Caption = "Max: " + (useMph ? Utils.KmToMiles(speedKph).ToString("0.0 mph") : speedKph.ToString("0.0 km/h"));
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
}
