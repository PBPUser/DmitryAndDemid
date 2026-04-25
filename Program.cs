using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid;


if (Configuration.Config.AlwaysAsk)
    new PreconfigWindow().Open();
else
{
    Runtime.CurrentRuntime = new Runtime();
    Runtime.CurrentRuntime.Start();
}

// E();

// async void E()
// {
//     A();
//     Console.WriteLine("Sample text");
//     while (true)
//         Console.Write("");
// }

// async Task A()
// {
//     while (true)
//     {
//         await Task.Delay(1000);
//         Console.WriteLine("1234");
//     }
// }
