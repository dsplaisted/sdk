using System;
using Coords;
namespace ManagedApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            Coord a = new Coord();
            Coord b = new Coord(39.0, 80.0);
            Console.WriteLine("Live from C# it is coords " + a.ToString() + " and " + b.ToString());
            Console.WriteLine("The distance was " + a.Distance(b));
        }
    }
}
