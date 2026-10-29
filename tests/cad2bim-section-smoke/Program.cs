using Dss.Cad2Bim.Core;

if (!SectionFootprintSelfTest.Run(out var message))
{
    Console.Error.WriteLine("FAIL: " + message);
    return 1;
}

Console.WriteLine("PASS: " + message);
return 0;
