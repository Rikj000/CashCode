using System.Collections.Generic;

namespace CashCode.Net
{
    public sealed class CashCodeErroList
    {
        public Dictionary<int, string> Errors { get; private set; }

        public CashCodeErroList()
        {
            Errors = new Dictionary<int, string>();

            Errors.Add(100000, "Unknown error");

            Errors.Add(100010, "Error opening Com port");
            Errors.Add(100020, "Com port is not open");
            Errors.Add(100030, "Error sending commands to enable the bill acceptor.");
            Errors.Add(100040, "Error sending the command to turn on the bill acceptor. The command POWER UP was not received from the bill acceptor.");
            Errors.Add(100050, "Error sending command to enable bill acceptor. ACK command not received from bill acceptor.");
            Errors.Add(100060, "Error sending commands to enable the bill acceptor. The command INITIALIZE was not received from the bill acceptor.");
            Errors.Add(100070, "Error checking the status of bill acceptor. Stacker removed.");
            Errors.Add(100080, "Error checking the status of the bill acceptor. The stacker is full.");
            Errors.Add(100090, "Error checking the status of a bill acceptor. A bill is stuck in the validator.");
            Errors.Add(100100, "Error checking the status of a bill acceptor. A bill is stuck in the stacker.");
            Errors.Add(100110, "Error checking the status of a bill acceptor. Fake bill.");
            Errors.Add(100120, "Error checking the status of a bill acceptor. The previous bill has not yet entered the stack and is in the recognition engine.");

            Errors.Add(100130, "Error of the bill acceptor. Failure during the operation of the mechanism of the stacker.");
            Errors.Add(100140, "Error of the bill acceptor. Failure in the transfer speed of the bill to the stacker.");
            Errors.Add(100150, "Error in the bill acceptor. The transfer of the bill to the stacker failed.");
            Errors.Add(100160, "Error in the bill acceptor. Failure of the bill leveling mechanism.");
            Errors.Add(100170, "Error of the bill acceptor. Failure in the work of the stacker.");
            Errors.Add(100180, "Error of the bill acceptor. Malfunction of optical sensors.");
            Errors.Add(100190, "Error of the bill acceptor. The inductance channel failed.");
            Errors.Add(100200, "Error of the bill acceptor. Malfunction of the stack checker channel failure.");

            // Bill Recognition Errors
            Errors.Add(0x60, "Rejecting due to Insertion");
            Errors.Add(0x61, "Rejecting due to Magnetic");
            Errors.Add(0x62, "Rejecting due to Remained bill in head");
            Errors.Add(0x63, "Rejecting due to Multiplying");
            Errors.Add(0x64, "Rejecting due to Conveying");
            Errors.Add(0x65, "Rejecting due to Identification1");
            Errors.Add(0x66, "Rejecting due to Verification");
            Errors.Add(0x67, "Rejecting due to Optic");
            Errors.Add(0x68, "Rejecting due to Inhibit");
            Errors.Add(0x69, "Rejecting due to Capacity");
            Errors.Add(0x6A, "Rejecting due to Operation");
            Errors.Add(0x6C, "Rejecting due to Length");
        }
    }
}
