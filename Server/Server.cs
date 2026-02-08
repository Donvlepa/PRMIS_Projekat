using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace Server
{
    public class Server
    {
        static List<Igrac> igraci = new List<Igrac>();
        static List<Socket> tcpKlijenti = new List<Socket>();
        static object lockObj = new object();
        static int sledecUdpPort = 6000;

        static void Main(string[] args)
        {
            Console.Title = "PingPong Server";
            Console.WriteLine("=== PingPong Turnirski Server ===");
            Console.WriteLine("Cekam prijavu igraca na portu 5000...");
            Console.WriteLine("Unesite broj igraca za turnir (min 4, paran broj):");

            int brojIgraca;
            while (true)
            {
                string unos = Console.ReadLine();
                if (int.TryParse(unos, out brojIgraca) && brojIgraca >= 4 && brojIgraca % 2 == 0)
                    break;
                Console.WriteLine("Unesite paran broj >= 4:");
            }

            // Kreiranje TCP soketa za osluskivanje
            Socket tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListener.Bind(new IPEndPoint(IPAddress.Any, 5000));
            tcpListener.Listen(10);
            Console.WriteLine($"Server pokrenut. Cekam {brojIgraca} igraca...");

            // Prijava igraca
            while (igraci.Count < brojIgraca)
            {
                Socket klijentSocket = tcpListener.Accept();

                // Primi podatke igraca
                byte[] buffer = new byte[4096];
                int bytesRead = klijentSocket.Receive(buffer);
                string podaci = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                string[] delovi = podaci.Split('|');
                string ime = delovi[0];
                string prezime = delovi[1];

                Igrac igrac = new Igrac(ime, prezime);
                lock (lockObj)
                {
                    igraci.Add(igrac);
                    tcpKlijenti.Add(klijentSocket);
                }

                // Posalji potvrdu
                string potvrda = $"Uspesna prijava! Dobrodosli {ime} {prezime}. Vi ste igrac br. {igraci.Count}";
                byte[] odgovor = Encoding.UTF8.GetBytes(potvrda);
                klijentSocket.Send(odgovor);

                Console.WriteLine($"Prijavljen igrac: {ime} {prezime} ({igraci.Count}/{brojIgraca})");
            }

            Console.WriteLine("\nSvi igraci prijavljeni! Pocinjem turnir...\n");

            // Posalji inicijalne podatke o igracima svim klijentima
            PosaljiSvimIgracima("TURNIR_POCETAK");
            Thread.Sleep(500);

            // Turnirski sistem
            PokreniTurnir();

            // Zavrsetak
            Console.WriteLine("\n=== Turnir zavrsen! ===");
            PrikaziRangListu();

            Console.WriteLine("\nPritisnite bilo koji taster za izlaz...");
            Console.ReadKey();
            tcpListener.Close();
        }

        static void PokreniTurnir()
        {
            // Nasumicno mesanje igraca
            Random rng = new Random();
            List<int> indeksi = Enumerable.Range(0, igraci.Count).OrderBy(x => rng.Next()).ToList();

            // Generisanje parova
            List<Thread> mecevi = new List<Thread>();
            List<ManualResetEvent> mecZavrsen = new List<ManualResetEvent>();

            for (int i = 0; i < indeksi.Count; i += 2)
            {
                int idx1 = indeksi[i];
                int idx2 = indeksi[i + 1];

                int udpPort1, udpPort2;
                lock (lockObj)
                {
                    udpPort1 = sledecUdpPort++;
                    udpPort2 = sledecUdpPort++;
                }

                Console.WriteLine($"Mec: {igraci[idx1].Ime} {igraci[idx1].Prezime} vs {igraci[idx2].Ime} {igraci[idx2].Prezime}");
                Console.WriteLine($"  UDP portovi: {udpPort1}, {udpPort2}");

                // Posalji info o mecu igracima putem TCP
                PosaljiIgracu(idx1, $"MEC|1|{udpPort1}|{udpPort2}|{igraci[idx1].Ime} {igraci[idx1].Prezime}|{igraci[idx2].Ime} {igraci[idx2].Prezime}");
                PosaljiIgracu(idx2, $"MEC|2|{udpPort1}|{udpPort2}|{igraci[idx1].Ime} {igraci[idx1].Prezime}|{igraci[idx2].Ime} {igraci[idx2].Prezime}");

                ManualResetEvent mre = new ManualResetEvent(false);
                mecZavrsen.Add(mre);

                int localIdx1 = idx1;
                int localIdx2 = idx2;
                int localPort1 = udpPort1;
                int localPort2 = udpPort2;
                ManualResetEvent localMre = mre;

                Thread t = new Thread(() =>
                {
                    OdigrajMec(localIdx1, localIdx2, localPort1, localPort2);
                    localMre.Set();
                });
                t.IsBackground = true;
                t.Start();
                mecevi.Add(t);
            }

            // Cekaj da se svi mecevi zavrse
            foreach (var mre in mecZavrsen)
            {
                mre.WaitOne();
            }

            // Posalji rang listu svim igracima
            PosaljiRangListuSvima();
        }

        static void OdigrajMec(int idx1, int idx2, int udpPort1, int udpPort2)
        {
            Igrac igrac1 = igraci[idx1];
            Igrac igrac2 = igraci[idx2];

            Mec mec = new Mec($"{igrac1.Ime} {igrac1.Prezime}", $"{igrac2.Ime} {igrac2.Prezime}");
            mec.Status = StatusIgre.UIgri;

            // UDP soketi za komunikaciju sa igracima
            Socket udpSocket1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket1.Bind(new IPEndPoint(IPAddress.Any, udpPort1));
            udpSocket1.ReceiveTimeout = 1;

            Socket udpSocket2 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket2.Bind(new IPEndPoint(IPAddress.Any, udpPort2));
            udpSocket2.ReceiveTimeout = 1;

            EndPoint remoteEP1 = new IPEndPoint(IPAddress.Any, 0);
            EndPoint remoteEP2 = new IPEndPoint(IPAddress.Any, 0);
            bool ep1Connected = false;
            bool ep2Connected = false;

            // Cekamo da se oba igraca jave na UDP
            Console.WriteLine($"Cekam UDP konekciju igraca za mec {igrac1.Ime} vs {igrac2.Ime}...");

            // Cekaj prvi paket od oba igraca da saznamo njihove endpoint-e
            byte[] recvBuf = new byte[4096];
            while (!ep1Connected)
            {
                try
                {
                    udpSocket1.ReceiveFrom(recvBuf, ref remoteEP1);
                    ep1Connected = true;
                }
                catch { }
            }

            while (!ep2Connected)
            {
                try
                {
                    udpSocket2.ReceiveFrom(recvBuf, ref remoteEP2);
                    ep2Connected = true;
                }
                catch { }
            }

            Console.WriteLine($"Oba igraca povezana! Pocinjem mec: {igrac1.Ime} vs {igrac2.Ime}");

            // Kretanje loptice
            double dx = 1;
            double dy = 0.5;

            // Game loop
            while (mec.Status == StatusIgre.UIgri)
            {
                // 1. Polling - primi komande od igraca (non-blocking)
                PrimiKomanduIgraca(udpSocket1, ref mec, 1);
                PrimiKomanduIgraca(udpSocket2, ref mec, 2);

                // 2. Pomeri lopticu
                mec.LopticaX += dx;
                mec.LopticaY += dy;

                // 3. Kolizija sa gornjom/donjom ivicom
                if (mec.LopticaY <= 0)
                {
                    mec.LopticaY = 0;
                    dy = -dy;
                }
                if (mec.LopticaY >= Mec.TEREN_VISINA - 1)
                {
                    mec.LopticaY = Mec.TEREN_VISINA - 1;
                    dy = -dy;
                }

                // 4. Kolizija sa reketom 1 (leva strana, x=1)
                if (mec.LopticaX <= 1 && dx < 0)
                {
                    int lopticaY = (int)Math.Round(mec.LopticaY);
                    if (lopticaY >= mec.PozicijaReketa1 && lopticaY < mec.PozicijaReketa1 + Mec.REKET_VELICINA)
                    {
                        dx = -dx;
                        mec.LopticaX = 1;
                    }
                }

                // 5. Kolizija sa reketom 2 (desna strana, x=TEREN_SIRINA-2)
                if (mec.LopticaX >= Mec.TEREN_SIRINA - 2 && dx > 0)
                {
                    int lopticaY = (int)Math.Round(mec.LopticaY);
                    if (lopticaY >= mec.PozicijaReketa2 && lopticaY < mec.PozicijaReketa2 + Mec.REKET_VELICINA)
                    {
                        dx = -dx;
                        mec.LopticaX = Mec.TEREN_SIRINA - 2;
                    }
                }

                // 6. Poentiranje
                if (mec.LopticaX <= 0)
                {
                    mec.RezultatIgrac2++;
                    igrac2.BrojOsvojenihBodova++;
                    ResetujLopticu(ref mec, ref dx, ref dy);
                }
                else if (mec.LopticaX >= Mec.TEREN_SIRINA - 1)
                {
                    mec.RezultatIgrac1++;
                    igrac1.BrojOsvojenihBodova++;
                    ResetujLopticu(ref mec, ref dx, ref dy);
                }

                // 7. Proveri kraj meca
                if (mec.RezultatIgrac1 >= Mec.MAX_POENI || mec.RezultatIgrac2 >= Mec.MAX_POENI)
                {
                    mec.Status = StatusIgre.Zavrsen;
                    if (mec.RezultatIgrac1 >= Mec.MAX_POENI)
                        igrac1.BrojPobeda++;
                    else
                        igrac2.BrojPobeda++;
                }

                // 8. Posalji stanje igre oboma igracima putem UDP
                byte[] stanje = mec.Serijalizuj();
                try
                {
                    udpSocket1.SendTo(stanje, remoteEP1);
                    udpSocket2.SendTo(stanje, remoteEP2);
                }
                catch { }

                // 9. Vizuelizacija na serveru
                IscrtajTeren(mec);

                // 10. Pauza (~20 FPS)
                Thread.Sleep(50);
            }

            // Posalji zavrsno stanje
            byte[] zavrsnoStanje = mec.Serijalizuj();
            try
            {
                udpSocket1.SendTo(zavrsnoStanje, remoteEP1);
                udpSocket2.SendTo(zavrsnoStanje, remoteEP2);
            }
            catch { }

            string pobednik = mec.RezultatIgrac1 >= Mec.MAX_POENI ? igrac1.Ime : igrac2.Ime;
            Console.WriteLine($"\nMec zavrsen! Pobednik: {pobednik}");
            Console.WriteLine($"Rezultat: {mec.Igrac1Ime} {mec.RezultatIgrac1} : {mec.RezultatIgrac2} {mec.Igrac2Ime}");

            udpSocket1.Close();
            udpSocket2.Close();
        }

        static void PrimiKomanduIgraca(Socket udpSocket, ref Mec mec, int igrackiBroj)
        {
            try
            {
                if (udpSocket.Available > 0)
                {
                    byte[] data = new byte[64];
                    EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    int received = udpSocket.ReceiveFrom(data, ref ep);
                    if (received > 0)
                    {
                        byte komanda = data[0];
                        if (igrackiBroj == 1)
                        {
                            if (komanda == 1 && mec.PozicijaReketa1 > 0)
                                mec.PozicijaReketa1--;
                            else if (komanda == 2 && mec.PozicijaReketa1 < Mec.TEREN_VISINA - Mec.REKET_VELICINA)
                                mec.PozicijaReketa1++;
                        }
                        else
                        {
                            if (komanda == 1 && mec.PozicijaReketa2 > 0)
                                mec.PozicijaReketa2--;
                            else if (komanda == 2 && mec.PozicijaReketa2 < Mec.TEREN_VISINA - Mec.REKET_VELICINA)
                                mec.PozicijaReketa2++;
                        }
                    }
                }
            }
            catch { }
        }

        static void ResetujLopticu(ref Mec mec, ref double dx, ref double dy)
        {
            mec.LopticaX = Mec.TEREN_SIRINA / 2;
            mec.LopticaY = Mec.TEREN_VISINA / 2;
            Random rng = new Random();
            dx = (rng.Next(2) == 0 ? 1 : -1);
            dy = (rng.NextDouble() - 0.5) * 1.5;
        }

        static void IscrtajTeren(Mec mec)
        {
            try
            {
                Console.SetCursorPosition(0, 0);
            }
            catch { return; }

            StringBuilder sb = new StringBuilder();

            // Rezultat iznad terena
            sb.AppendLine($"  {mec.Igrac1Ime}  {mec.RezultatIgrac1} : {mec.RezultatIgrac2}  {mec.Igrac2Ime}");
            sb.AppendLine($"  Status: {mec.Status}");
            sb.AppendLine("+" + new string('-', Mec.TEREN_SIRINA) + "+");

            int lopticaX = (int)Math.Round(mec.LopticaX);
            int lopticaY = (int)Math.Round(mec.LopticaY);

            for (int y = 0; y < Mec.TEREN_VISINA; y++)
            {
                sb.Append("|");
                for (int x = 0; x < Mec.TEREN_SIRINA; x++)
                {
                    // Reket 1 (leva strana, x=0)
                    if (x == 0 && y >= mec.PozicijaReketa1 && y < mec.PozicijaReketa1 + Mec.REKET_VELICINA)
                    {
                        sb.Append("|");
                    }
                    // Reket 2 (desna strana, x=TEREN_SIRINA-1)
                    else if (x == Mec.TEREN_SIRINA - 1 && y >= mec.PozicijaReketa2 && y < mec.PozicijaReketa2 + Mec.REKET_VELICINA)
                    {
                        sb.Append("|");
                    }
                    // Loptica
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

        static void PosaljiIgracu(int indeks, string poruka)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(poruka);
                tcpKlijenti[indeks].Send(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greska pri slanju igracu {indeks}: {ex.Message}");
            }
        }

        static void PosaljiSvimIgracima(string poruka)
        {
            for (int i = 0; i < tcpKlijenti.Count; i++)
            {
                PosaljiIgracu(i, poruka);
            }
        }

        static void PosaljiRangListuSvima()
        {
            PrikaziRangListu();

            StringBuilder sb = new StringBuilder();
            sb.Append("RANG_LISTA|");

            var sortiraniIgraci = igraci.OrderByDescending(i => i.BrojPobeda)
                                       .ThenByDescending(i => i.BrojOsvojenihBodova)
                                       .ToList();

            for (int i = 0; i < sortiraniIgraci.Count; i++)
            {
                var ig = sortiraniIgraci[i];
                sb.Append($"{i + 1}.{ig.Ime} {ig.Prezime},{ig.BrojPobeda},{ig.BrojOsvojenihBodova}");
                if (i < sortiraniIgraci.Count - 1)
                    sb.Append(";");
            }

            string poruka = sb.ToString();
            PosaljiSvimIgracima(poruka);
        }

        static void PrikaziRangListu()
        {
            Console.Clear();
            Console.WriteLine("\n===== RANG LISTA =====");
            Console.WriteLine($"{"#",-4}{"Ime",-15}{"Prezime",-15}{"Pobede",-10}{"Bodovi",-10}");
            Console.WriteLine(new string('-', 54));

            var sortiraniIgraci = igraci.OrderByDescending(i => i.BrojPobeda)
                                       .ThenByDescending(i => i.BrojOsvojenihBodova)
                                       .ToList();

            for (int i = 0; i < sortiraniIgraci.Count; i++)
            {
                var ig = sortiraniIgraci[i];
                Console.WriteLine($"{i + 1,-4}{ig.Ime,-15}{ig.Prezime,-15}{ig.BrojPobeda,-10}{ig.BrojOsvojenihBodova,-10}");
            }
            Console.WriteLine("======================\n");
        }
    }
}
