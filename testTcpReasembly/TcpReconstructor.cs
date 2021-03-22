using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;


// Translated from the file follow.c from WireShark source code
// the code can be found at: http://www.wireshark.org/download.html

namespace TcpReconstructor
{
    /* here we are going to try and reconstruct the data portion of a TCP
   session. We will try and handle duplicates, TCP fragments, and out
   of order packets in a smart way. */

    /// <summary>
    /// A class that represent a node in a linked list that holds partial Tcp session
    /// fragments
    /// </summary>
    internal class tcp_frag
    {
        public ulong seq = 0;
        public ulong len = 0;
        public ulong data_len = 0;
        public byte[] data = null;
        public tcp_frag next = null;
    };


    public class TcpRecon
    {
        // holds two linked list of the session data, one for each direction    
        tcp_frag frags = null;

        // holds the last sequence number for each direction
        ulong seq = 0;

        bool empty_tcp_stream = true;

        private uint bytes_written = 0;
        System.IO.FileStream data_out_file = null;
        bool incomplete_tcp_stream = false;
        bool closed = false;
        private bool first = true;

        public bool IncompleteStream
        {
            get { return incomplete_tcp_stream; }
        }
        public bool EmptyStream
        {
            get { return empty_tcp_stream; }
        }

        public TcpRecon(int connection)
        {
            reset_tcp_reassembly();
            data_out_file = new System.IO.FileStream("data/" + connection.ToString() + ".txt", System.IO.FileMode.Create);


        }

        /// <summary>
        /// Cleans up the class and frees resources
        /// </summary>
        public void Close()
        {
            if (!closed)
            {
                data_out_file.Close();
                reset_tcp_reassembly();
                closed = true;
            }
        }

        ~TcpRecon()
        {
            Close();
        }

        /// <summary>
        /// The main function of the class receives a tcp packet and reconstructs the stream
        /// </summary>
        /// <param name="tcpPacket"></param>
        public void ReassemblePacket(TcpDatagram tcpPacket)
        {
            // if the tcpPayload length is zero bail out
            var length = (uint)tcpPacket.PayloadLength;
            if (length == 0) return;

            var tcpPayload = tcpPacket.Payload.ToArray();

            reassemble_tcp(tcpPacket.SequenceNumber,
                tcpPayload, length, tcpPacket.IsSynchronize);
        }

        /// <summary>
        /// Writes the payload data to the file
        /// </summary>
        /// <param name="index"></param>
        /// <param name="data"></param>
        private void write_packet_data(byte[] data, ulong seq)
        {
            // ignore empty packets
            if (data.Length == 0) return;

            Console.WriteLine(seq);
            data_out_file.Write(data, 0, data.Length);
            data_out_file.Flush();


            bytes_written += (uint)data.Length;
            empty_tcp_stream = false;
        }

        /// <summary>
        /// Reconstructs the tcp session
        /// </summary>
        /// <param name="sequence">Sequence number of the tcp packet</param>
        /// <param name="length">The size of the original packet data</param>
        /// <param name="data">The captured data</param>
        /// <param name="data_length">The length of the captured data</param>
        /// <param name="synflag"></param>
        /// <param name="net_src">The source ip address</param>
        /// <param name="net_dst">The destination ip address</param>
        /// <param name="srcport">The source port</param>
        /// <param name="dstport">The destination port</param>
        private void reassemble_tcp(ulong sequence, byte[] data, ulong data_length, bool synflag)
        {
            var s = sequence;
            ulong newseq;
            tcp_frag tmp_frag;


            /* now that we have filed away the srcs, lets get the sequence number stuff
            figured out */
            if (first)
            {

                /* this is the first time we have seen this src's sequence number */
                seq = sequence + data_length;
                if (synflag)
                {
                    seq++;
                }

                /* write out the packet data */
                write_packet_data(data, s);

                first = false;
                return;
            }

            /* if we are here, we have already seen this src, let's
            try and figure out if this packet is in the right place */
            if (sequence < seq)
            {
                /* this sequence number seems dated, but
                check the end to make sure it has no more
                info than we have already seen */
                newseq = sequence + data_length;
                if (newseq > seq)
                {
                    ulong new_len;

                    /* this one has more than we have seen. let's get the
                    payload that we have not seen. */

                    new_len = seq - sequence;
                    
                    data_length -= new_len;
                    byte[] tmpData = new byte[data_length];
                    for (ulong i = 0; i < data_length; i++)
                        tmpData[i] = data[i + new_len];

                    data = tmpData;

                    sequence = seq;
                    data_length = newseq - seq;

                    /* this will now appear to be right on time :) */
                }
            }

            if (sequence == seq)
            {
                /* right on time */
                seq += data_length;
                if (synflag) seq++;
                if (data != null)
                {
                    write_packet_data(data, s);
                }

                /* done with the packet, see if it caused a fragment to fit */
                while (check_fragments());
            }
            else
            {
                /* out of order packet */
                if (data_length > 0 && sequence > seq)
                {
                    tmp_frag = new tcp_frag();
                    tmp_frag.data = data;
                    tmp_frag.seq = sequence;
                    tmp_frag.len = data_length;
                    tmp_frag.data_len = data_length;

                    if (frags != null)
                    {
                        tmp_frag.next = frags;
                    }
                    else
                    {
                        tmp_frag.next = null;
                    }

                    frags = tmp_frag;
                }
            }
        } /* end reassemble_tcp */

        /* here we search through all the frag we have collected to see if
        one fits */
        bool check_fragments()
        {
            tcp_frag prev = null;
            tcp_frag current;
            current = frags;
            while (current != null)
            {
                if (current.seq == seq)
                {
                    /* this fragment fits the stream */
                    if (current.data != null)
                    {
                        write_packet_data(current.data, 0);
                    }

                    seq += current.len;

                    if (prev != null)
                    {
                        prev.next = current.next;
                    }
                    else
                    {
                        frags = current.next;
                    }

                    current.data = null;
                    current = null;
                    return true;
                }

                if (current.seq < seq && current.data != null)
                {
                    var newseq = current.seq + current.data_len;
                    if (newseq > seq)
                    {
                        ulong new_len;

                        new_len = seq - current.seq;

                        if (current.data_len > new_len)
                        {
                            var copyLength = current.data_len -= new_len;
                            byte[] tmpData = new byte[copyLength];
                            for (ulong i = 0; i < copyLength; i++)
                                tmpData[i] = current.data[i + new_len];

                            write_packet_data(tmpData, 0);
                        }

                        seq = newseq;

                        if (prev != null)
                        {
                            prev.next = current.next;
                        }
                        else
                        {
                            frags = current.next;
                        }

                        return true;
                    }
                }

                prev = current;
                current = current.next;
            }

            return false;
        }

        // cleans the linked list
        void reset_tcp_reassembly()
        {
            tcp_frag current, next;
            int i;

            empty_tcp_stream = true;
            incomplete_tcp_stream = false;
            
            seq = 0;
            bytes_written = 0;
            current = frags;
            while (current != null)
            {
                next = current.next;
                current.data = null;
                current = null;
                current = next;
            }

            frags = null;
            
        }

    }

}
