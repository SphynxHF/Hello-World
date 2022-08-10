using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Helloworld
{
    class Program
    {
        static void Main(string[] args)
        {
            // Defines the wait for usewr input then saves it as the string "name"
            string name = Console.ReadLine();

            // Says hello world and hello "user input"
            Console.WriteLine("Hello, World And hello {0}", name);

            // allows user the option to terminate by pressing enter
            Console.WriteLine("Press Enter to teminate");
            // Pauses the program
            Console.Read();
        }
    }
}
