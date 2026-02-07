using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Domain
{
    [Serializable]
    public enum StatusIgre
    {
        CekaSe,
        UIgri,
        Zavrsen
    }

    [Serializable]
    public class Mec
    {
        public const int TEREN_SIRINA = 40;
        public const int TEREN_VISINA = 20;
        public const int REKET_VELICINA = 4;
        public const int MAX_POENI = 10;

        public int PozicijaReketa1 { get; set; }
        public int PozicijaReketa2 { get; set; }

        public double LopticaX { get; set; }
        public double LopticaY { get; set; }

        public int RezultatIgrac1 { get; set; }
        public int RezultatIgrac2 { get; set; }

        public string Igrac1Ime { get; set; }
        public string Igrac2Ime { get; set; }

        public StatusIgre Status { get; set; }

        public Mec() { }

        public Mec(string igrac1Ime, string igrac2Ime)
        {
            Igrac1Ime = igrac1Ime;
            Igrac2Ime = igrac2Ime;
            PozicijaReketa1 = TEREN_VISINA / 2 - REKET_VELICINA / 2;
            PozicijaReketa2 = TEREN_VISINA / 2 - REKET_VELICINA / 2;
            LopticaX = TEREN_SIRINA / 2;
            LopticaY = TEREN_VISINA / 2;
            RezultatIgrac1 = 0;
            RezultatIgrac2 = 0;
            Status = StatusIgre.CekaSe;
        }

        public byte[] Serijalizuj()
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                bf.Serialize(ms, this);
                return ms.ToArray();
            }
        }

        public static Mec Deserijalizuj(byte[] data)
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream(data))
            {
                return (Mec)bf.Deserialize(ms);
            }
        }

        public override string ToString()
        {
            return $"{Igrac1Ime} {RezultatIgrac1} : {RezultatIgrac2} {Igrac2Ime} | Status: {Status}";
        }
    }
}
