using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid;
using DmitryAndDemid.Utils;

if (Configuration.Config.AlwaysAsk)
    new PreconfigWindow().Open();
else
{
    Runtime.CurrentRuntime = new Runtime();
    Runtime.CurrentRuntime.Start();
}
