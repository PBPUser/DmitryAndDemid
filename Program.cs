using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid;
using DmitryAndDemid.Utils;
using Microsoft.CodeAnalysis.CSharp.Scripting;

Console.WriteLine(await CSharpScript.EvaluateAsync<int>("X+Y", globals: new Globals { X = 1, Y = 2 }));

if (Configuration.Config.AlwaysAsk)
    new PreconfigWindow().Open();
else
{
    Runtime.CurrentRuntime = new Runtime();
    Runtime.CurrentRuntime.Start();
}
