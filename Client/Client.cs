using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Domain;

namespace Client
{
    public class Client
    {
        static void Main(string[] args)
        {
            Console.Title = "PingPong Klijent";
            Console.WriteLine("=== PingPong Klijent ===\n");

            // Unos podataka
            Console.Write("Unesite ime: ");
            string ime = Console.ReadLine();
            Console.Write("Unesite prezime: ");
            string prezime = Console.ReadLine();

            Socket tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                tcpSocket.Connect(new IPEndPoint(IPAddress.Loopback, 5000));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greska pri povezivanju na server: {ex.Message}");
                Console.ReadKey();
                return;
            }

            // Posalji ime i prezime
            string podaci = $"{ime}|{prezime}";
            byte[] data = Encoding.UTF8.GetBytes(podaci);
            tcpSocket.Send(data);

            // Primi potvrdu
            byte[] buffer = new byte[4096];
            int bytesRead = tcpSocket.Receive(buffer);
            string potvrda = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine(potvrda);
            Console.WriteLine("\nCekam pocetak turnira...\n");

            // Cekaj poruku o pocetku turnira
            bytesRead = tcpSocket.Receive(buffer);
            string turniPoruka = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            if (turniPoruka.StartsWith("TURNIR_POCETAK"))
            {
                Console.WriteLine("Turnir je poceo! Cekam raspored meceva...");
            }

            // Cekaj info o mecu
            bytesRead = tcpSocket.Receive(buffer);
            string mecInfo = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            if (mecInfo.StartsWith("MEC"))
            {
                string[] delovi = mecInfo.Split('|');
                int igrackiBroj = int.Parse(delovi[1]);
                int udpPort1 = int.Parse(delovi[2]);
                int udpPort2 = int.Parse(delovi[3]);
                string igrac1Ime = delovi[4];
                string igrac2Ime = delovi[5];

                Console.WriteLine($"\nMec: {igrac1Ime} vs {igrac2Ime}");
                Console.WriteLine($"Vi ste igrac br. {igrackiBroj}");
                Console.WriteLine("Koristite strelice GORE/DOLE za pomeranje reketa.");
                Console.WriteLine("Mec pocinje za 3 sekunde...");
                Thread.Sleep(3000);

                // Pokreni igru
                PokreniIgru(igrackiBroj, udpPort1, udpPort2, igrac1Ime, igrac2Ime);
            }

            // Cekaj rang listu
            Console.WriteLine("\nCekam rang listu...");
            try
            {
                bytesRead = tcpSocket.Receive(buffer);
                if (bytesRead > 0)
                {
                    string rangListaPoruka = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    if (rangListaPoruka.StartsWith("RANG_LISTA"))
                    {
                        PrikaziRangListu(rangListaPoruka);
                    }
                }
            }
            catch { }

            Console.WriteLine("\nPritisnite bilo koji taster za izlaz...");
            Console.ReadKey();
            tcpSocket.Close();
        }

        static void PokreniIgru(int igrackiBroj, int udpPort1, int udpPort2, string igrac1Ime, string igrac2Ime)
        {
            int mojPort = igrackiBroj == 1 ? udpPort1 : udpPort2;
            EndPoint serverEP = new IPEndPoint(IPAddress.Loopback, mojPort);

            // UDP soket za komunikaciju sa serverom
            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Posalji inicijalni paket serveru da se registrujemo
            byte[] helo = new byte[] { 0 };
            udpSocket.SendTo(helo, serverEP);

            Console.Clear();

            // Game loop
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    byte komanda = 0;
                    if (key.Key == ConsoleKey.UpArrow)
                        komanda = 1; // gore
                    else if (key.Key == ConsoleKey.DownArrow)
                        komanda = 2; // dole

                    if (komanda > 0)
                    {
                        byte[] cmd = new byte[] { komanda };
                        udpSocket.SendTo(cmd, serverEP);
                    }
                }

                // Polling
                if (udpSocket.Available > 0)
                {
                    try
                    {
                        byte[] stanjeBuf = new byte[4096];
                        EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                        int received = udpSocket.ReceiveFrom(stanjeBuf, ref ep);

                        byte[] stanje = new byte[received];
                        Array.Copy(stanjeBuf, stanje, received);
                        Mec mec = Mec.Deserijalizuj(stanje);

                        IscrtajTeren(mec, igrackiBroj);

                        if (mec.Status == StatusIgre.Zavrsen)
                        {
                            Console.SetCursorPosition(0, Mec.TEREN_VISINA + 5);
                            string pobednik = mec.RezultatIgrac1 >= Mec.MAX_POENI ? mec.Igrac1Ime : mec.Igrac2Ime;
                            Console.WriteLine($"\n*** MEC ZAVRSEN! Pobednik: {pobednik} ***");
                            Console.WriteLine($"Rezultat: {mec.Igrac1Ime} {mec.RezultatIgrac1} : {mec.RezultatIgrac2} {mec.Igrac2Ime}");
                            break;
                        }
                    }
                    catch { }
                }

                Thread.Sleep(10);
            }

            udpSocket.Close();
        }

        static void IscrtajTeren(Mec mec, int igrackiBroj)
        {
            try
            {
                Console.SetCursorPosition(0, 0);
            }
            catch { return; }

            StringBuilder sb = new StringBuilder();

            // Rezultat iznad terena
            sb.AppendLine($"  {mec.Igrac1Ime}  {mec.RezultatIgrac1} : {mec.RezultatIgrac2}  {mec.Igrac2Ime}");
            string strana = igrackiBroj == 1 ? "LEVO" : "DESNO";
            sb.AppendLine($"  Vi ste igrac {igrackiBroj} ({strana}) | Strelice GORE/DOLE za pomeranje");
            sb.AppendLine("+" + new string('-', Mec.TEREN_SIRINA) + "+");

            int lopticaX = (int)Math.Round(mec.LopticaX);
            int lopticaY = (int)Math.Round(mec.LopticaY);

            for (int y = 0; y < Mec.TEREN_VISINA; y++)
            {
                sb.Append("|");
                for (int x = 0; x < Mec.TEREN_SIRINA; x++)
                {
                    if (x == 0 && y >= mec.PozicijaReketa1 && y < mec.PozicijaReketa1 + Mec.REKET_VELICINA)
                    {
                        sb.Append("|");
                    }
                    else if (x == Mec.TEREN_SIRINA - 1 && y >= mec.PozicijaReketa2 && y < mec.PozicijaReketa2 + Mec.REKET_VELICINA)
                    {
                        sb.Append("|");
                    }
                    else if (x == lopticaX && y == lopticaY)
                    {
                        sb.Append("O");
                    }
                    else
                    {
                        sb.Append(" ");
                    }
                }
                sb.AppendLine("|");
            }
            sb.AppendLine("+" + new string('-', Mec.TEREN_SIRINA) + "+");

            Console.Write(sb.ToString());
        }

        static void PrikaziRangListu(string poruka)
        {
            Console.Clear();
            // Format: RANG_LISTA|1.Ime Prezime,Pobede,Bodovi;2.Ime Prezime,Pobede,Bodovi
            string[] delovi = poruka.Split('|');
            if (delovi.Length < 2) return;

            string[] igraci = delovi[1].Split(';');

            Console.WriteLine("\n===== RANG LISTA =====");
            Console.WriteLine($"{"#",-4}{"Ime i Prezime",-25}{"Pobede",-10}{"Bodovi",-10}");
            Console.WriteLine(new string('-', 49));

            foreach (string igrac in igraci)
            {
                // Format: 1.Ime Prezime,Pobede,Bodovi
                int tackaPoz = igrac.IndexOf('.');
                string rang = igrac.Substring(0, tackaPoz);
                string ostalo = igrac.Substring(tackaPoz + 1);
                string[] podatci = ostalo.Split(',');

                string imeIPrezime = podatci[0];
                string pobede = podatci[1];
                string bodovi = podatci[2];

                Console.WriteLine($"{rang,-4}{imeIPrezime,-25}{pobede,-10}{bodovi,-10}");
            }
            Console.WriteLine("======================");
        }
    }
}
