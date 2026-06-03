using DmitryAndDemid;

if (Configuration.Config.AlwaysAsk)
    new PreconfigWindow().Open();
else
{
    Runtime.CurrentRuntime = new Runtime();
    Runtime.CurrentRuntime.Start();
}
