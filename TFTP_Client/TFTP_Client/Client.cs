﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO; 
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Windows.Forms;
using System.Net.Sockets;
using TFTP_Client.States;


namespace TFTP_Client
{
     
    class Client
    {
        
        private String host;
        private static Int16 DATA_PACKET_SIZE = 512;
        //private Socket sock = null;
        private Int16 dstPort = 69;
        
        private String retrPath;
        private static byte[] DELIMITER = new byte[] { 0x00 }; 
        private FileStream sendingFileStream = null;
        private FileStream receivingFileStream = null;
        private String sendingFilename = null;
        private UdpClient udpClient;
        private IPEndPoint endpoint;
        private String currentLog;
        //virtual ports
        private int localVirtualPort; 
         
        private IPEndPoint ipEndPoint;
        byte[] sendingPacket;
        int bytesToBeSend;

        private TFTPClientWindow context;

        public void setRetrPath(String p) {
            this.retrPath = sanitizePath(p.Trim());
        }

        private String sanitizePath(String s) {

            if (!s.EndsWith("\\"))
            { 
                s += "\\"; 
            }
            return s;

        }

        public void setContext(TFTPClientWindow context) {
            this.context = context;
        }

        public void setSendingFilename(String f) {
            this.sendingFilename = f;
        }

        /**
         *   OPERATION CODES RFC1350
         * 
         *   1     Read request (RRQ)
         *   2     Write request (WRQ)
         *   3     Data (DATA)
         *   4     Acknowledgment (ACK)
         *   5     Error (ERROR)
         */
        static class OpCode {
            public static readonly short ReadReq = 0x01,
             WriteReq = 0x02,
             Data = 0x03,
             Ack = 0x04,
             Error = 0x05;
        };
        
        ClientState clientState;

        private static Client _instance;

        public static Client getInstance() {

            //a static access for the states, so they can apply the next state
            if (_instance == null) {

                //for the public static access getInstance we set the variable first
                _instance = new Client();
                _instance.sendingPacket = new byte[2052];
                 
            }
            
            return _instance;
        }

        private Client() {
            
            //The first time we have to generate a state on our own and not by the state mechanism
            //Then we go into the init-Method according to the state
            clientState = new InitState();
 
        }
          

        public void put(String filename, String host, String port)
        {
            try
            {
                //always ensure that we are in the init state. 
                //the current state would throw an exception if it is not the initstate (put would not be applicable)
                this.clientState.put();
                this.sendingFilename = filename;
                this.host = host;
                this.dstPort = Int16.Parse(port);

                //change to send state and send a write request
                connectInitial();
                sendWriteRequest();

                Console.WriteLine("waiting for ack");
                //wait for acknowledgement
                receiveOptionAcknowledgment();

            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine("Error: " + e);
            }
        }
        public void get(String filename, String host, String port)
        {
            try
            {
                //always ensure that we are in the init state. 
                //the current state would throw an exception if it is not the initstate (put would not be applicable)
                this.clientState.get();
                this.sendingFilename = filename;
                this.host = host;
                this.dstPort = Int16.Parse(port);

                //change to send state and send a write request
                connectInitial();
                sendReadRequest();

                Console.WriteLine("waiting for ack");
                //wait for acknowledgement
                receiveOptionAcknowledgment();

            } catch (InvalidOperationException e) {
                Console.WriteLine("Error: " + e);
            }
        }

        /**
         * The first two fields of this array should be assigned. this will be the int16 (short). the rest will be ignored.
         */
        private short toShort(byte[] buf)
        {
            //buf should contain 2 values little endian
            return (short)(buf[0] * 256 + buf[1]);
        }

        private void receiveOptionAcknowledgment() {

            udpClient.Client.Close();
            udpClient = new UdpClient(localVirtualPort);
            
            endpoint = new IPEndPoint(IPAddress.Parse(host), 0);

            udpClient.Client.ReceiveTimeout = 30000;

            byte[] receiveBytes = udpClient.Receive(ref endpoint);

            //Console.WriteLine("output " + receiveBytes.Length);
            
            //the operation code should be 6
            if (toShort(receiveBytes) == 6)
            {
                //ok 
                //everythings alright

                System.Type state = this.clientState.GetType();

                if (state == typeof(PutState))
                {
                    if (MessageBox.Show("The server acknowledged the Write Request. Would you like to continue uploading?", "Continue", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        Console.WriteLine("beginUploadingDataPackets");
                        
                        System.Threading.Thread newThread = new System.Threading.Thread(this.send);
                        newThread.Start();
                        
                    }
                    else {

                        resetClient();
                        
                    }
                    
                }
                else if (state == typeof(GetState))
                {

                    //LIKE in the Lecture
                    if (MessageBox.Show("The server agreed sending you the file. Would you like to continue?", "Continue", MessageBoxButtons.YesNo) != DialogResult.Yes)
                    {
                        resetClient();
                        return;
                    } 

                    udpClient.Connect(endpoint);
                    //send acknowledge <BLOCK 0>
                    startPacket();
                    sendOpCode(OpCode.Ack);
                    sendOpCode(0x00); //send the block no. 0
                    commit();

                    //go into receive ;)
                    System.Threading.Thread newThread = new System.Threading.Thread(this.receive);
                    newThread.Start();
                    
                }
                 
            }
            else {


                if (toShort(receiveBytes) == 5)
                {
                    //bad case
                    Console.WriteLine("ERROR Code 5 from Server: " + toShort(receiveBytes));
                }
                else {
                    //bad case
                    Console.WriteLine("Unknown operation from Server: " + toShort(receiveBytes));
                
                }
                resetClient();
            }

        }

        void resetClient()
        {  //reset client
            udpClient.Close();
            udpClient = null;
            this.clientState = new InitState();
        
        }
        void writeToFile(byte[] buf) {
            if (receivingFileStream == null) {
                receivingFileStream = File.OpenWrite(this.retrPath + this.sendingFilename);
            }
            receivingFileStream.Write(buf, 0, buf.Length);
        }
      
        void receive()
        {
            int blockCount = 0;
            byte[] data;
 
            //receive data
            while (true)
            {

                //set state receive
                clientState.receive();

                 
                data = udpClient.Receive(ref endpoint);
                blockCount++;
                byte[] blockNumber = Utils.partByteArray(data, 2, 4);

//                Console.WriteLine("output " + data.Length);
                if (toShort(blockNumber) == blockCount && toShort(data) == OpCode.Data)
                {
                     
                    //everything alright, we can save the data
                    writeToFile(Utils.partByteArray(data, 4,data.Length));

                    //sent ack
                    clientState.ack();

                    startPacket();
                    sendOpCode(OpCode.Ack);
                    sendOpCode((short)blockCount); //send the block no. 0
                    commit();

                    //check if download finished
                    if (data.Length < DATA_PACKET_SIZE + 4)
                    {
                        Console.WriteLine("Finished transfer");
                        receivingFileStream.Close();
                        receivingFileStream = null;
                        resetClient();
                        break;
                    }
                }
                else
                {
                    resetClient();
                    Console.WriteLine("ERROR RECEIVING DATA");
                    break;
                }

            }
        
        }
         

        private void send()
        {
            
            udpClient.Connect(endpoint);

            int dataGramCount = 1 + ((int)(new FileInfo(sendingFilename).Length)) / DATA_PACKET_SIZE;
            int receivedAcks = 0;

            for (int i = 1; i <= dataGramCount; i++)
            {

                //change the state to sending an then send the data block #i
                this.clientState.send();
                this.startPacket();
                this.sendDataBlock(i);
                this.commit();
                int timedOutCountInARow = 0;
                this.clientState.ack();
                udpClient.Client.ReceiveTimeout = 5000;
                byte[] ack;

                try
                {
                    ack = udpClient.Receive(ref endpoint);

                    //get the block nr in the ack frame
                    byte[] blockNr = Utils.partByteArray(ack, 2, 4);

                    //reset  timedOutCountInARow on success
                    timedOutCountInARow = 0;

                    if (toShort(ack) == 4 && toShort(blockNr) == i)
                    {

                        //good one
                        receivedAcks++;

                    }
                    else
                    {
                        throw new InvalidDataException("invalid data received: received " + toShort(ack) + " and " + toShort(blockNr) + ", but expected: 4 and " + i);
                    }

                }
                catch (SocketException e)
                {

                    if (e.SocketErrorCode == SocketError.TimedOut)
                    {
                        //retransmission! //set the filepointer
                        if (sendingFileStream.CanSeek == false)
                        {
                            Console.WriteLine("sorry, the filestream cannot seek! FATAL ERROR!");
                        }
                        sendingFileStream.Seek((i - 1) * DATA_PACKET_SIZE, SeekOrigin.Begin);
                        i--;
                        timedOutCountInARow++;
                        if (timedOutCountInARow >= 3)
                        {
                            break;
                        }

                    }
                    else
                    {

                        Console.WriteLine("error: " + e.Message);

                    }
                }

            }

            if (receivedAcks == dataGramCount)
            {

                Console.WriteLine("Package sent successfully");
                //go back to init state
                this.clientState = new InitState();
                sendingFileStream.Close();
                sendingFileStream = null;

            }
        
        }


        //Setter and getter for ClientState
        public void setClientState(ClientState state){
            this.clientState = state;
        }

        /* maybe not needed
        public ClientState getClientState(){
            return this.clientState;
        }
        */

        /**
         * This sends a data block from the file
         * 
         * From http://tools.ietf.org/html/rfc1350 Sending a
         * DATA packet is an acknowledgment for the first ACK packet of the
         * previous DATA packet. The WRQ and DATA packets are acknowledged by
         * ACK or ERROR packets, while RRQ
         */
        private void sendDataBlock(int blockNumber)
        {

            //as we have a standard of about DATA_PACKET_SIZE bytes we send them immediately, we use the same file pointer again
            //lets first read the data into an array of bytes
            if (sendingFileStream == null)
                sendingFileStream = File.OpenRead(sendingFilename);
              
            byte[] blockData = new byte[DATA_PACKET_SIZE];
            
            int bytes = sendingFileStream.Read(blockData, 0, DATA_PACKET_SIZE);
              
            //send the operation code for sending a file 
            sendOpCode(OpCode.Data);

            //since the block number is contained in the same format like the operation code (2 bytes) we can use the same method
            sendOpCode((short)blockNumber);

            //send the data
            appendToSendingPacket(blockData, bytes);

        }



        /**
         * This method sends a write request to the server, with the given Filename
         * Argument must include the full path to the file
         */
        private void sendWriteRequest()
        {

            startPacket();

            sendOpCode(OpCode.WriteReq);

            //Send the filename string into the socket
            sendString(Path.GetFileName(sendingFilename), true, true);

            //send octet format information
            sendString("octet", true, true);

            //send size information
            sendString("tsize", true, true);

            //send the actual size
            sendIntAsString(new FileInfo(sendingFilename).Length, true, true);

            //send blksize information key
            sendString("blksize", true, true);

            //send blksize information value
            sendIntAsString(DATA_PACKET_SIZE, true, true);

            //send timeout information 
            sendString("timeout", true, true);

            //send timeout information value (as string!)
            sendString("30", true, true);

            //send resume information key
            sendString("x-resume", true, true);

            //send resume information value
            sendString("0", true, true);

            commit(true);
        }

        private void sendIntAsString(Int64 a, bool withDel, bool forLogging)
        {
            sendString(a.ToString(), withDel, forLogging);
        }

        private void sendIntAsString(Int64 a, bool withDel)
        {
            sendIntAsString(a, withDel, false);
        }

        /**
         * This method sends a read request to the server, with the given Filename
         * Argument must include the full path to the file
         */
        private void sendReadRequest()
        {

            startPacket();

            sendOpCode(OpCode.ReadReq);

            //Send the filename string into the socket
            sendString(Path.GetFileName(sendingFilename), true, true);

            //send octet format information
            sendString("octet", true, true);

            //send size information  //key
            sendString("tsize", true, true);

            //send the actual size //value
            sendString("0", true, true);

            //send blksize information key
            sendString("blksize", true, true);

            //send blksize information value
            sendIntAsString(DATA_PACKET_SIZE, true, true);

            //send timeout information 
            sendString("timeout", true, true);

            //send timeout information value (as string!)
            sendString("30", true, true);

            /* commented as not seen in protocol this time :(
            //send resume information key
            sendString("x-resume", true);

            //send resume information value
            sendString("0", true);
            */

            commit(true);
        }

        private void commit(bool log)
        {
            if (log) {

                context.protocolMSGInv(currentLog);

            }
            currentLog = "";
            udpClient.Send(sendingPacket, bytesToBeSend);
            bytesToBeSend = 0;
        }
        private void commit()
        {
            commit(false);
        }

        private void sendString(String str, bool withDelimiter, bool forLogging) {

            byte[] msg = Encoding.ASCII.GetBytes(str);
            
            try
            {
                appendToSendingPacket(msg, msg.Length);

                if (withDelimiter)
                    //send the delimiter which is the byte 0x00
                    appendToSendingPacket(DELIMITER, 1);

                //since we only use this for small
                if (forLogging)
                {
                    currentLog = currentLog + str + (withDelimiter ? "." : "");
                }
            }
            catch (SocketException e) {
                Console.Write("Exception in writing socket string: " + e.Message);
            }

            

        }

        private byte[] shortToBytes(short number){
            byte[] b = new byte[2];
            
            b[0] = (byte)(number >> 8);
            b[1] = (byte)(number & 255);

            return b;
        }

        private void sendOpCode(short operationCode)
        {
            //the short consists of about 2 bytes, little endian
            
            //convert the short to a byte array
            byte[] opcode = shortToBytes(operationCode);

            //put into the socket
            appendToSendingPacket(opcode, 2);
            
        }

        private void appendToSendingPacket(byte[] arr, int length)
        {
            
            for (int i = 0; i < length; i++) { 
                sendingPacket[bytesToBeSend+i] = arr[i];
            }

            bytesToBeSend += length;
        }

        private void startPacket()
        {
            bytesToBeSend = 0;
        }
 
        private void connectInitial()
        {
            try
            {

                //set a new endpoint
                ipEndPoint = new IPEndPoint(IPAddress.Parse(host), dstPort);

                //assign a new udp Client with the given endpoint to the sock instance variable
                udpClient = new UdpClient();

                udpClient.Connect(ipEndPoint);

                localVirtualPort = ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
                 
            }
            catch (SocketException e)
            {
                Console.WriteLine("FATAL, cannot connect to host with address " + host + " " + e.Message);
            }
        }
    }
}
