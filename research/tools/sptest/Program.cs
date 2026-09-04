using System; using System.Diagnostics; using System.IO.Ports;
class P { static void Main() {
  try {
    using var sp = new SerialPort("COM3", 19200, Parity.Mark, 8, StopBits.Two);
    sp.WriteTimeout = 100; sp.Open(); var s = sp.BaseStream;
    for (int i=0;i<50;i++){ sp.Parity = (i%2==0)?Parity.Space:Parity.Mark; }   // warm up
    var sw = Stopwatch.StartNew();
    for (int i=0;i<2000;i++) sp.Parity = (i%2==0)?Parity.Space:Parity.Mark;
    sw.Stop();
    Console.WriteLine("SetCommState (parity swap) x2000 = " + sw.Elapsed.TotalMilliseconds.ToString("F2")
      + " ms => " + (sw.Elapsed.TotalMilliseconds*1000/2000).ToString("F1") + " us each");
    sw.Restart();
    for (int i=0;i<2000;i++) s.Flush();     // FlushFileBuffers on an empty TX queue
    sw.Stop();
    Console.WriteLine("BaseStream.Flush (FlushFileBuffers, empty TX) x2000 = " + sw.Elapsed.TotalMilliseconds.ToString("F2")
      + " ms => " + (sw.Elapsed.TotalMilliseconds*1000/2000).ToString("F1") + " us each");
    Console.WriteLine("NOTE: COM3 is a Bluetooth virtual port with no paired peer; numbers are API-overhead only, not a real UART.");
  } catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); }
} }
