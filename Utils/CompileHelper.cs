using System.CodeDom.Compiler;
using System.Diagnostics;
using Microsoft.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DmitryAndDemid.Utils;

public class CompileHelper
{
    public static async void Generate()
    {
        var result = await CSharpScript.EvaluateAsync<int>("1+2");
        
        await CSharpScript.EvaluateAsync("using System; Console.WriteLine(\"fhetghe\");");
        Console.WriteLine(result);
    }
}