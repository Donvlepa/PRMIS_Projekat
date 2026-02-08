using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Domain
{
    public class Igrac
    {
        public string Ime { get; set; }
        public string Prezime { get; set; }
        public int BrojPobeda { get; set; }
        public int BrojOsvojenihBodova { get; set; }

        public Igrac() { }

        public Igrac(string ime, string prezime)
        {
            Ime = ime;
            Prezime = prezime;
            BrojPobeda = 0;
            BrojOsvojenihBodova = 0;
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

        public static Igrac Deserijalizuj(byte[] data)
        {
            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream(data))
            {
                return (Igrac)bf.Deserialize(ms);
            }
        }

        public override string ToString()
        {
            return $"{Ime} {Prezime} | Pobede: {BrojPobeda} | Bodovi: {BrojOsvojenihBodova}";
        }
    }
}
