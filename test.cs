using System;
using Valve.VR;
class Program { 
    static void Main() {
        var err = EVRInitError.None;
        Console.WriteLine("Trying Background...");
        var sys = OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
        Console.WriteLine("Result: " + err);
    }
}
