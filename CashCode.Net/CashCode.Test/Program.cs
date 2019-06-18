using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CashCode.Net;

namespace CashCodeTest
{
    class Program
    {
        static int Sum = 0;

        static void Main(string[] args)
        {
            try
            {
                using (CashCodeBillValidator c = new CashCodeBillValidator("COM4", 9600))
                {
                    c.BillReceived += new BillReceivedHandler(c_BillReceived);
                    c.BillStacking += new BillStackingHandler(c_BillStacking);
                    c.BillCassetteStatusEvent += new BillCassetteHandler(c_BillCassetteStatusEvent);
                    c.ConnectBillValidator();

                    if (c.IsConnected)
                    {
                        c.PowerUpBillValidator();
                        c.StartListening();

                        c.EnableBillValidator();
                        Console.ReadKey();
                        c.DisableBillValidator();
                        c.StopListening();
                    }

                    c.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static void c_BillCassetteStatusEvent(object Sender, BillCassetteEventArgs e)
        {
            Console.WriteLine(e.Status.ToString());
        }

        static void c_BillStacking(object Sender, System.ComponentModel.CancelEventArgs e)
        {
            Console.WriteLine("Bill in stack");
            if (Sum > 100)
            {
                //e.Cancel = true;
                Console.WriteLine("One-time payment limit exceeded");
            }
        }

        static void c_BillReceived(object Sender, BillReceivedEventArgs e)
        {
            if (e.Status == BillRecievedStatus.Rejected)
            {
                Console.WriteLine(e.RejectedReason);
            }
            else if (e.Status == BillRecievedStatus.Accepted)
            {
                Sum += e.Value;
                // Kan zijn dat programma niet out of the box met euro's werkt maar met russische roebels
                Console.WriteLine("Bill accepted! " + e.Value + " euro. Total amount: " + Sum.ToString());
            }
        }


    }
}
