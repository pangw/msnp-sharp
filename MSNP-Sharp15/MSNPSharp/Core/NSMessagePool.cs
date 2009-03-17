#region Copyright (c) 2002-2009, Bas Geertsema, Xih Solutions (http://www.xihsolutions.net), Thiago.Sayao, Pang Wu, Ethem Evlice
/*
Copyright (c) 2002-2009, Bas Geertsema, Xih Solutions
(http://www.xihsolutions.net), Thiago.Sayao, Pang Wu, Ethem Evlice.
All rights reserved. http://code.google.com/p/msnp-sharp/

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice,
  this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
* Neither the names of Bas Geertsema or Xih Solutions nor the names of its
  contributors may be used to endorse or promote products derived from this
  software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS 'AS IS'
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
THE POSSIBILITY OF SUCH DAMAGE. 
*/
#endregion

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

namespace MSNPSharp.Core
{
    using MSNPSharp;
    using MSNPSharp.DataTransfer;

    /// <summary>
    /// Buffers the incoming data from the notification server (NS).
    /// </summary>
    /// <remarks>
    /// The main purpose of this class is to ensure that MSG, IPG and NOT payload commands are processed
    /// only when they are complete. Payload commands can be quite large and may be larger
    /// than the socket buffer. This pool will buffer the data and release the messages, or commands,
    /// when they are fully retrieved from the server.
    /// </remarks>
    public class NSMessagePool : MessagePool, IDisposable
    {
        Queue<MemoryStream> messageQueue = new Queue<MemoryStream>();
        MemoryStream bufferStream;
        BinaryWriter bufferWriter;
        int remainingBuffer;

        public NSMessagePool()
        {
            CreateNewBuffer();
        }

        ~NSMessagePool()
        {
            Dispose(false);
        }

        /// <summary>
        /// Is true when there are message available to retrieve.
        /// </summary>
        public override bool MessageAvailable
        {
            get
            {
                return messageQueue.Count > 0;
            }
        }

        protected Queue<MemoryStream> MessageQueue
        {
            get
            {
                return messageQueue;
            }
        }

        /// <summary>
        /// This points to the current message we are writing to.
        /// </summary>
        protected MemoryStream BufferStream
        {
            get
            {
                return bufferStream;
            }
            set
            {
                bufferStream = value;
            }
        }

        /// <summary>
        /// This is the interface to the bufferStream.
        /// </summary>
        protected BinaryWriter BufferWriter
        {
            get
            {
                return bufferWriter;
            }
            set
            {
                bufferWriter = value;
            }
        }

        /// <summary>
        /// Creates a new memorystream to server as the buffer.
        /// </summary>
        private void CreateNewBuffer()
        {
            bufferStream = new MemoryStream(64);
            bufferWriter = new BinaryWriter(bufferStream);
        }

        /// <summary>
        /// Enques the current buffer memorystem when a message is completely retrieved.
        /// </summary>
        private void EnqueueCurrentBuffer()
        {
            messageQueue.Enqueue(bufferStream);
        }

        /// <summary>
        /// Get the next message as a byte array. The returned data includes all newlines which seperate the commands ("\r\n")
        /// </summary>
        /// <returns></returns>
        public override byte[] GetNextMessageData()
        {
            return messageQueue.Dequeue().ToArray();
        }

        /// <summary>
        /// Stores the raw data in a buffer. When a full message is detected it is inserted on the internal stack.
        /// You can retrieve these messages bij calling GetNextMessageData().
        /// </summary>
        /// <param name="reader"></param>
        public override void BufferData(BinaryReader reader)
        {
            int length = (int)(reader.BaseStream.Length - reader.BaseStream.Position);

            // there is nothing in the bufferstream so we expect a command right away
            while (length > 0)
            {
                // should we buffer the current message
                if (remainingBuffer > 0)
                {
                    // read as much as possible in the current message stream
                    int readLength = Math.Min(remainingBuffer, length);
                    byte[] msgBuffer = reader.ReadBytes(readLength);
                    bufferStream.Write(msgBuffer, 0, msgBuffer.Length);

                    // subtract what we have read from the total length
                    remainingBuffer -= readLength;
                    length = (int)(reader.BaseStream.Length - reader.BaseStream.Position);

                    // when we have read everything we can start a new message
                    if (remainingBuffer == 0)
                    {
                        EnqueueCurrentBuffer();
                        CreateNewBuffer();
                    }
                }
                else
                {
                    // read until we come across a newline
                    byte val = reader.ReadByte();

                    if (val != '\n')
                        bufferWriter.Write(val);
                    else
                    {
                        // write the last newline
                        bufferWriter.Write(val);

                        // check if it's a payload command
                        bufferStream.Position = 0;
                        byte[] cmd = new byte[3] { 
                            (byte)bufferStream.ReadByte(),
                            (byte)bufferStream.ReadByte(),
                            (byte)bufferStream.ReadByte() 
                        };

                        if ((cmd[0] == 'M' && cmd[1] == 'S' && cmd[2] == 'G') ||    // MSG payload command
                           (cmd[0] == 'I' && cmd[1] == 'P' && cmd[2] == 'G') ||     // IPG pager command
                           (cmd[0] == 'N' && cmd[1] == 'O' && cmd[2] == 'T') ||     // NOT notification command
                           (cmd[0] == 'U' && cmd[1] == 'B' && cmd[2] == 'X') ||     // UBX personal message
                           (cmd[0] == 'G' && cmd[1] == 'C' && cmd[2] == 'F') ||     // GCF privacy settings
                           (cmd[0] == 'U' && cmd[1] == 'B' && cmd[2] == 'M') ||     // UBM Yahoo messenger message
                           (cmd[0] == 'U' && cmd[1] == 'B' && cmd[2] == 'N') ||     // UBN Unified Budy Notification (for SIP requests)
                           (cmd[0] == 'U' && cmd[1] == 'U' && cmd[2] == 'N') ||     // UUN Unified User Notification
                           (cmd[0] == 'A' && cmd[1] == 'D' && cmd[2] == 'L') ||     // ADL Add List command
                           (cmd[0] == 'R' && cmd[1] == 'M' && cmd[2] == 'L') ||     // RML Remove List command
                           (cmd[0] == 'F' && cmd[1] == 'Q' && cmd[2] == 'Y') ||     // FQY Forward QuerY command
                           (cmd[0] == '2' && cmd[1] == '0' && cmd[2] == '4') ||     // 204 Invalid contact network in ADL/RML (payload)
                           (cmd[0] == '2' && cmd[1] == '4' && cmd[2] == '1') ||     // 241 Invalid membership for ADL/RML (payload)
                           (cmd[0] == '5' && cmd[1] == '0' && cmd[2] == '9'))       // 509 UpsFailure, when sending mobile message (payload)
                        {
                            bufferStream.Seek(-3, SeekOrigin.End);

                            // calculate the length by reading backwards from the end
                            remainingBuffer = 0;
                            int size = 0;

                            for (int i = 0; ((size = bufferStream.ReadByte()) > 0) && size >= '0' && size <= '9'; i++)
                            {
                                remainingBuffer += (int)((size - '0') * Math.Pow(10, i));
                                bufferStream.Seek(-2, SeekOrigin.Current);
                            }

                            // move to the end of the stream before we are going to write
                            bufferStream.Seek(0, SeekOrigin.End);

                            if (remainingBuffer == 0)
                            {
                                EnqueueCurrentBuffer();
                                CreateNewBuffer();
                            }
                        }
                        else
                        {
                            // it was just a plain command start a new message
                            EnqueueCurrentBuffer();
                            CreateNewBuffer();
                        }
                    }
                    length--;
                }
            }
        }

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose managed resources
                if (bufferWriter != null)
                    ((IDisposable)bufferWriter).Dispose();

                if (bufferStream != null)
                    bufferStream.Dispose();

                if (messageQueue.Count > 0)
                    messageQueue.Clear();
            }

            // Free native resources
        }


        #endregion
    }
};