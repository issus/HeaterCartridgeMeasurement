using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

Console.WriteLine("BambuLab Heater Characterization");

// Keysight 34465A Client
TcpClient ksClient = new TcpClient("10.0.0.34", 5025);
var ksStream = ksClient.GetStream();
var ksWriter = new StreamWriter(ksStream);
var ksReader = new StreamReader(ksStream);


//Rigol DP832A Client
TcpClient dpClient = new TcpClient("10.0.0.32", 5555);
var dpStream = dpClient.GetStream();
var dpWriter = new StreamWriter(dpStream);
var dpReader = new StreamReader(dpStream);


ksWriter.WriteLine("*IDN?");
ksWriter.Flush();
string ksIdn = ksReader.ReadLine().Trim();
Console.WriteLine(ksIdn);


dpWriter.WriteLine("*IDN?");
dpWriter.Flush();
string dpIdn = dpReader.ReadLine().Trim();
Console.WriteLine(dpIdn);


ksWriter.WriteLine("CONF:TEMP TC,K");
ksWriter.Flush();


ksWriter.WriteLine("READ?");
ksWriter.Flush();
var dmmTempResponse = ksReader.ReadLine();
Decimal.TryParse(dmmTempResponse, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal startTemp);


decimal dpVoltage = 2;

// Set CH1 of DP832A to 5V so we can get an initial reading of room temperature current
dpWriter.WriteLine($"SOUR1:VOLT {dpVoltage:F2}");
dpWriter.Flush();
//dpWriter.WriteLine("OUTP CH1,ON");
//dpWriter.Flush();

decimal peakTemperature = startTemp - 1;
decimal previousTemperature;


List<(decimal Temperature, decimal Resistance)> readings = new();

while (peakTemperature < 320)
{
    dpWriter.WriteLine("OUTP CH1,ON");
    dpWriter.Flush();

    await Task.Delay(500);
    
    Stopwatch elapsed = Stopwatch.StartNew();

    decimal current = 0;
    decimal voltage = 0;
    decimal temperature = peakTemperature;

    decimal tempAt1Sec = 0;

    // heat until temperature is 2 degrees above previous peak
    while (temperature < peakTemperature + 2)
    {
        //await Task.Delay(110);

        dpWriter.WriteLine("MEAS:CURR? CH1");
        dpWriter.Flush();
        Decimal.TryParse(dpReader.ReadLine(), NumberStyles.Any, CultureInfo.InvariantCulture, out current);

        dpWriter.WriteLine("MEAS:VOLT? CH1");
        dpWriter.Flush();
        Decimal.TryParse(dpReader.ReadLine(), NumberStyles.Any, CultureInfo.InvariantCulture, out voltage);

        ksWriter.WriteLine("READ?");
        ksWriter.Flush();
        Decimal.TryParse(ksReader.ReadLine(), NumberStyles.Any, CultureInfo.InvariantCulture, out temperature);

        if (temperature > 320)
            break;

        // maintain a temperature delta of 0.5-1 degree per second
        if (tempAt1Sec == 0 && elapsed.ElapsedMilliseconds > 2000)
        {
            tempAt1Sec = temperature;
            elapsed.Restart();
        }
        else if (elapsed.ElapsedMilliseconds > 2000 && dpVoltage < 24)
        {
            var delta = (temperature - tempAt1Sec) / (elapsed.ElapsedMilliseconds / 1000);

            if (delta < 0.5m)
            {
                dpVoltage += 0.2m;
            }
            else if (delta > 1)
            {
                dpVoltage -= 0.5m;
            }

            dpWriter.WriteLine($"SOUR1:VOLT {dpVoltage:F1}");
            dpWriter.Flush();

            tempAt1Sec = 0;

            elapsed.Restart();
        }
    }

    peakTemperature = temperature;

    // heat has exceeded previous peak temperature
    dpWriter.WriteLine("OUTP CH1,OFF");
    dpWriter.Flush();

    // shouldnt be needed with the new temperature delta rate. Keeping for faster temperature deltas.
    // continue reading temperature to find peak from previous heating cycle
    do
    {
        previousTemperature = temperature;

        ksWriter.WriteLine("READ?");
        ksWriter.Flush();
        Decimal.TryParse(ksReader.ReadLine(), NumberStyles.Any, CultureInfo.InvariantCulture, out temperature);
        
        if (temperature > peakTemperature)
        {
            peakTemperature = temperature;
        }
    } while (temperature > previousTemperature);

    if (current > 0)
    {
        decimal resistance = voltage / current;
        readings.Add((peakTemperature, resistance));
        Console.WriteLine($"Temperature: {peakTemperature:F2}°C, Resistance: {resistance:F2}Ω");
    }
}

await using var csvWriter = new StreamWriter(@"C:\temp\heater.csv");
await csvWriter.WriteLineAsync(ksIdn);
await csvWriter.WriteLineAsync(dpIdn);
await csvWriter.WriteLineAsync();
await csvWriter.WriteLineAsync("Temperature,Resistance");
foreach (var (temperature, resistance) in readings)
{
    await csvWriter.WriteLineAsync($"{temperature:F2},{resistance:F4}");
}

Console.WriteLine("Characterization completed. CSV file written to C:\\temp\\heater.csv");