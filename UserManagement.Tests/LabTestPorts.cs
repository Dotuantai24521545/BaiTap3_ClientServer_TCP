using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace UserManagement.Tests
{
    internal static class LabTestPorts
    {
        private static IEnumerable<int> Candidates()
        {
            var random = new Random(Guid.NewGuid().GetHashCode());
            for (int i = 0; i < 128; i++) yield return random.Next(1024, 49152);
        }
        private static TcpListener Listen(IPAddress address, IEnumerable<int> candidates)
        {
            foreach (int port in candidates) {
                if (port < 1024 || port > 49151) throw new ArgumentOutOfRangeException(nameof(candidates));
                var listener = new TcpListener(address, port) { ExclusiveAddressUse = true };
                try { listener.Start(); return listener; }
                catch (SocketException error) {
                    listener.Stop();
                    if (error.SocketErrorCode != SocketError.AddressAlreadyInUse) throw;
                }
            }
            throw new IOException("No unused test port was found in the required 1024..49151 range.");
        }
        public static TcpListener Listen(IPAddress address) { return Listen(address, Candidates()); }
        public static int AvailableServerPort()
        {
            var probe = Listen(IPAddress.Any);
            try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
            finally { probe.Stop(); }
        }
        public static int Verify()
        {
            // Exercises the same NumericUpDown boundary that rejected ephemeral port 52153.
            var busy = Listen(IPAddress.Loopback);
            TcpListener selected = null;
            try {
                int busyPort = ((IPEndPoint)busy.LocalEndpoint).Port;
                var candidates = new List<int> { busyPort }; candidates.AddRange(Candidates());
                selected = Listen(IPAddress.Loopback, candidates);
                int selectedPort = ((IPEndPoint)selected.LocalEndpoint).Port;
                if (selectedPort == busyPort) throw new InvalidOperationException("An occupied port was reused.");
                Console.WriteLine("PASS port selection skips an occupied listener");
                int serverPort = AvailableServerPort();
                using (var field = new NumericUpDown { Minimum = 1024, Maximum = 49151 }) {
                    field.Value = serverPort; field.Value = selectedPort;
                    Console.WriteLine("PASS server and proxy ports fit the actual UI 1024..49151 constraint");
                }
                bool rejected = false;
                try { var invalid = Listen(IPAddress.Loopback, new[] { 52153 }); invalid.Stop(); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Out-of-range port was accepted.");
                Console.WriteLine("PASS invalid ephemeral port 52153 is rejected before binding");
                return 0;
            } finally { if (selected != null) selected.Stop(); busy.Stop(); }
        }
    }
}
