using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTAVMod_Speedometer
{
    enum AccelerationTimerState
    {
        Off = 0, // off
        WaitingForStop = 1, // veh not stopped
        Ready = 2, // veh stopped, timer ready
        Counting = 3, // veh accelerating, timer counting
        Finished = 4, // veh reached 100kph, timer stops
    }
}
