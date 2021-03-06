﻿#region License
// ====================================================
// EasySSA Copyright(C) 2017 Furkan Türkal
// This program comes with ABSOLUTELY NO WARRANTY; This is free software,
// and you are welcome to redistribute it under certain conditions; See
// file LICENSE, which is part of this source code package, for details.
// ====================================================

//Reference -> https://www.elitepvpers.com/forum/sro-coding-corner/3983169-re-release-clientless-client-sample.html (Thanks DexterSoul)
#endregion

using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

using EasySSA.SSA;
using EasySSA.Common;
using EasySSA.Packets;
using EasySSA.Component;
using EasySSA.Core.Network.Securities;
using System.Threading.Tasks;
using EasySSA.Core.Network;
using System.Threading;

namespace EasySSA.Context {
    public sealed class SROClientContext {

        private SROClientComponent ClientComponent;

        private SROClient m_client;

        private SROClientLoader m_loader;

        private SROServiceComponent m_gatewayComponent;

        private SROServiceComponent m_agentComponent;

        private List<Shard> m_shards;

        public SROClientContext(SROClientComponent clientComponent) {
            this.ClientComponent = clientComponent;
            this.m_shards = new List<Shard>();
        }

        public void Initialize() {
            this.SetupComponents();
            this.RegisterListeners();
            this.RegisterGatewayPacketListener();
            this.RegisterAgentPacketListener();
        }

        private void SetupComponents() {
            this.m_gatewayComponent = new SROServiceComponent(ServerServiceType.GATEWAY, 1)
                            .SetFingerprint(new Fingerprint("GatewayServer", 0, SecurityFlags.Handshake & SecurityFlags.Blowfish & SecurityFlags.SecurityBytes, ""))
                            .SetLocalEndPoint(this.ClientComponent.LocalGatewayEndPoint)
                            .SetLocalBindTimeout(10)
                            .SetServiceEndPoint(this.ClientComponent.ServiceEndPoint)
                            .SetServiceBindTimeout(100)
                            .SetMaxClientCount(500)
                            .SetDebugMode(false);
            this.m_agentComponent = new SROServiceComponent(ServerServiceType.AGENT, 1)
                            .SetFingerprint(new Fingerprint("AgentServer", 0, SecurityFlags.Handshake & SecurityFlags.Blowfish & SecurityFlags.SecurityBytes, ""))
                            .SetLocalEndPoint(this.ClientComponent.LocalAgentEndPoint)
                            .SetLocalBindTimeout(10)
                            .SetServiceBindTimeout(100)
                            .SetMaxClientCount(500)
                            .SetDebugMode(false);
        }

        private void RegisterListeners() {
            if(this.m_gatewayComponent != null) {
                this.m_gatewayComponent.OnLocalSocketStatusChanged += new Action<SocketError>(delegate (SocketError error) {
                    if (error == SocketError.Success) {
                        Console.WriteLine("[GATEWAY LISTENER] LOCAL socket bind SUCCESS! : " + this.m_gatewayComponent.LocalEndPoint.ToString());
                    } else {
                        Console.WriteLine("[GATEWAY LISTENER] LOCAL socket bind FAILED!  : " + error);
                    }
                });
                this.m_gatewayComponent.OnServiceSocketStatusChanged += new Action<SROClient, SocketError>(delegate (SROClient client, SocketError error) {
                    if (error == SocketError.Success) {
                        Console.WriteLine("[GATEWAY LISTENER] REMOTE service socket connect SUCCESS! : " + this.m_gatewayComponent.ServiceEndPoint.ToString());
                    } else {
                        Console.WriteLine("[GATEWAY LISTENER] REMOTE service socket connect FAILED!  : " + error);
                    }
                });
                this.m_gatewayComponent.OnClientConnected += new Func<SROClient, bool>(delegate (SROClient client) {
                    Console.WriteLine("[GATEWAY LISTENER] New client connected : " + client.Socket.RemoteEndPoint);
                    this.m_client = client;
                    this.m_client.IsClientless = this.ClientComponent.IsClientless;
                    return true;
                });
                this.m_gatewayComponent.OnClientDisconnected += new Action<SROClient, ClientDisconnectType>(delegate (SROClient client, ClientDisconnectType disconnectType) {
                    Console.WriteLine("[GATEWAY LISTENER] Client disconnected : " + client.IPAddress + " -- Reason : " + disconnectType);
                });
            }

            if(m_agentComponent != null){
                this.m_agentComponent.OnLocalSocketStatusChanged += new Action<SocketError>(delegate (SocketError error) {
                    if (error == SocketError.Success) {
                        Console.WriteLine("[AGENT LISTENER] LOCAL socket bind SUCCESS! : " + this.m_agentComponent.LocalEndPoint.ToString());
                    } else {
                        Console.WriteLine("[AGENT LISTENER] LOCAL socket bind FAILED!  : " + error);
                    }
                });

                this.m_agentComponent.OnServiceSocketStatusChanged += new Action<SROClient, SocketError>(delegate (SROClient client, SocketError error) {
                    if (error == SocketError.Success) {
                        Console.WriteLine("[AGENT LISTENER] REMOTE service socket connect SUCCESS! : " + this.m_agentComponent.ServiceEndPoint.ToString());
                    } else {
                        Console.WriteLine("[AGENT LISTENER] REMOTE service socket connect FAILED!  : " + error);
                    }
                });

                this.m_agentComponent.OnClientConnected += new Func<SROClient, bool>(delegate (SROClient client) {
                    Console.WriteLine("[AGENT LISTENER] New client connected : " + client.Socket.RemoteEndPoint);
                    return true;
                });

                this.m_agentComponent.OnClientDisconnected += new Action<SROClient, ClientDisconnectType>(delegate (SROClient client, ClientDisconnectType disconnectType) {
                    Console.WriteLine("[AGENT LISTENER] Client disconnected : " + client.IPAddress + " -- Reason : " + disconnectType);
                });
            }
        }

        private void RegisterGatewayPacketListener() {
            if(this.m_gatewayComponent != null){
                this.m_gatewayComponent.OnPacketReceived += new Func<SROClient, SROPacket, PacketSocketType, PacketResult>(delegate (SROClient client, SROPacket packet, PacketSocketType socketType) {

                    

                    this.ClientComponent.OnPacketReceived?.Invoke(client, packet, socketType);

                    //Received from CLIENT
                    if (socketType == PacketSocketType.CLIENT) {
                        return GetPacketResultOnClientPacketReceived(client, packet);

                    //Received from REMOTE
                    } else if (socketType == PacketSocketType.SERVER) {
                        if (this.m_client.IsClientless) {
                            if (!this.m_client.CanSwitchClient) {

                                //Request Patch
                                if (packet.Opcode == 0x2001) {
                                    if (packet.ReadAscii() == "GatewayServer") {
                                        Packet response = new Packet(0x6100, true);
                                        response.WriteByte(this.m_client.LocaleID);
                                        response.WriteAscii("SR_Client");
                                        response.WriteUInt(this.m_client.VersionID);
                                        return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(response));
                                    }
                                        
                                }

                                //Reconnect to AgentServer on successfull login
                                if (packet.Opcode == 0xA100) {
                                    byte result = packet.ReadByte();
                                    if (result == 1) {
                                        this.m_client.SessionID = packet.ReadUInt();
                                        this.m_client.AgentIP = packet.ReadAscii();
                                        this.m_client.AgentPort = packet.ReadUShort();

                                        //CanDoAgentServerConnect = true;                        

                                        //this.m_agentComponent.DOConnect(new IPEndPoint(IPAddress.Parse(this.m_client.AgentIP), this.m_client.AgentPort), delegate (bool status, BindErrorType error) {

                                        //    Console.WriteLine("m_agentComponent DOConnect status : " + status);
                                        //    Console.WriteLine("m_agentComponent DOConnect error : " + error);

                                        //});

                                        //this.m_gatewayComponent.UNBind();
                                        //this.m_agentComponent.SetServiceEndPoint(new IPEndPoint(IPAddress.Parse(this.m_client.AgentIP), this.m_client.AgentPort));
                                        //this.m_agentComponent.DOBind(delegate (bool success, BindErrorType error) {
                                        //    if (success) {
                                        //        Console.WriteLine("CONTEXT.Connect() AGENT PROXY bind SUCCESS");
                                        //    } else {
                                        //        Console.WriteLine("AGENT bind FAILED -- Reason : " + error);
                                        //    }
                                        //});
                                    }
                                }

                                if (packet.Opcode == 0xA101) {
                                    //GlobalServer
                                    bool nextGlobalServer = packet.ReadBool();
                                    do {
                                        packet.ReadByte(); //GlobalOperationID
                                        packet.ReadAscii(); //GlobalOperationName

                                        nextGlobalServer = packet.ReadBool();
                                    } while (nextGlobalServer);

                                    //ShardList
                                    bool nextShard = packet.ReadBool();
                                    this.m_shards.Clear();
                                    do {
                                        this.m_shards.Add(new Shard(packet));
                                        nextShard = packet.ReadBool();
                                    } while (nextShard);
                                }

                                //Request Server List
                                if (packet.Opcode == 0xA102) {
                                    byte errorCode = packet.ReadByte();
                                    if (errorCode == 1) { //Connected to Agent Server
                                        this.ClientComponent.OnAccountStatusChanged?.Invoke(client, AccountStatusType.LOGIN_SUCCESS);

                                        Packet response = new Packet(0x6101, true);
                                        return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(response));
                                    } else if (errorCode == 2) { //Cannot connect Agent Server.
                                        switch (packet.ReadUInt8()) {
                                            //Wrong ID/PW
                                            case 1: {
                                                    byte totalattempts = packet.ReadUInt8();
                                                    byte num1 = packet.ReadUInt8();
                                                    byte num2 = packet.ReadUInt8();
                                                    byte num3 = packet.ReadUInt8();
                                                    byte attempts = packet.ReadUInt8();

                                                    this.ClientComponent.OnAccountStatusChanged?.Invoke(client, AccountStatusType.LOGIN_FAILED);
                                                    break;
                                                }

                                            //Account action
                                            case 2:

                                                //Blocked
                                                if (packet.ReadUInt8() == 1) {
                                                    this.ClientComponent.OnAccountStatusChanged?.Invoke(client, AccountStatusType.BANNED);
                                                    //Reason -> packet.ReadAscii());
                                                }
                                                break;

                                            //Character is already logged on
                                            case 3:
                                                this.ClientComponent.OnAccountStatusChanged?.Invoke(client, AccountStatusType.ALREADY_LOGGED_ON);
                                                break;

                                            //Maybe offline or error ?
                                            default: {
                                                    this.ClientComponent.OnAccountStatusChanged?.Invoke(client, AccountStatusType.LOGIN_FAILED);
                                                    break;
                                                }
                                        }
                                    } else {
                                        Console.WriteLine("There is an update or you are using an invalid silkroad version.");
                                        return new PacketResult(PacketOperationType.IGNORE);
                                    }
                                }



                                //CAPTCHA
                                if (packet.Opcode == 0x2322) {
                                    this.ClientComponent.OnCaptchaStatusChanged?.Invoke(client, CaptchaStatusType.FETCH);

                                    //var captcha = Captcha.GeneratePacketCaptcha(packet);
                                    //Captcha.SaveCaptchaToBMP(captcha, Environment.CurrentDirectory + "\\captcha.bmp");
                                    //Program.main.picCaptcha.Image = Bitmap.FromFile(Environment.CurrentDirectory + "\\captcha.bmp");
                                    //Program.main.groupImageCode.Enabled = true;
                                }


                                if (packet.Opcode == 0xA323) {
                                    byte result = packet.ReadByte();
                                    if(result == 1) {
                                        this.ClientComponent.OnCaptchaStatusChanged?.Invoke(client, CaptchaStatusType.CORRECT);
                                    } else {
                                        this.ClientComponent.OnCaptchaStatusChanged?.Invoke(client, CaptchaStatusType.WRONG);
                                    }
                                    //if (result != 1) {
                                    //    //Get captcha image ?
                                    //    this.m_client.CanCaptchaCheck = true;

                                    //    Packet response = this.ClientComponent.GetCaptchaPacket();
                                    //    return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(response));

                                    //} else if (result == 2) {
                                    //    //Wrong Captcha
                                    //    this.m_client.CanCaptchaCheck = false;
                                    //}
                                }

                            }
                        } else {
                            #region Redirect client to local AgentListener

                            if (packet.Opcode == 0xA102) {
                                byte result = packet.ReadByte();
                                if (result == 1) {
                                    this.m_client.SessionID = packet.ReadUInt();
                                    this.m_client.AgentIP = packet.ReadAscii();
                                    this.m_client.AgentPort = packet.ReadUShort();


                                    //Create fake response for Client to redirect to localIP/localPort
                                    SROPacket response = new SROPacket(0xA102, true);
                                    response.WriteByte(result);
                                    response.WriteUInt(this.m_client.SessionID);
                                    response.WriteAscii(this.ClientComponent.LocalAgentEndPoint.Address.ToString());
                                    response.WriteUShort((ushort)this.ClientComponent.LocalAgentEndPoint.Port);
                                    //response.WriteAscii(System.Net.IPAddress.Loopback.ToString());
                                    //response.WriteUShort(this.m_client.LocalPort);
                                    response.Lock();
                                    this.m_client.IsConnectedToAgent = true;
                                    this.m_agentComponent.SetServiceEndPoint(new IPEndPoint(IPAddress.Parse(this.m_client.AgentIP), this.m_client.AgentPort));
                                    this.m_agentComponent.DOBind(delegate (bool success, BindErrorType error) {
                                        if (success) {
                                            Console.WriteLine("[AGENT LISTENER] PROXY bind SUCCESS");
                                        } else {
                                            Console.WriteLine("[AGENT LISTENER] PROXY bind FAILED -- Reason : " + error);
                                        }
                                    });

                                    return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(response));
                                    //packet = response;
                                } else {
                                    this.m_client.CanAccountLogin = true;
                                }

                                client.SendPacket(packet);
                            }

                            #endregion
                        }
                    }

                    return new PacketResult(PacketOperationType.NOTHING);
                });
            }
        }

        private void RegisterAgentPacketListener() {
            if(this.m_agentComponent != null) {
                this.m_agentComponent.OnPacketReceived += new Func<SROClient, SROPacket, PacketSocketType, PacketResult>(delegate (SROClient client, SROPacket packet, PacketSocketType socketType) {
                    this.ClientComponent.OnPacketReceived?.Invoke(client, packet, socketType);

                    if (socketType == PacketSocketType.CLIENT) {
                        return GetPacketResultOnClientPacketReceived(client, packet);
                    } else if (socketType == PacketSocketType.SERVER) {
                        if (this.m_client.IsClientless) {
                            if (!this.m_client.CanSwitchClient) {

                                if (packet.Opcode == 0x6005) {
                                    Packet response = new Packet(0x6103, true);
                                    response.WriteUInt(this.m_client.SessionID);
                                    response.WriteAscii(this.m_client.Account.Username);
                                    response.WriteAscii(this.m_client.Account.Password);
                                    response.WriteByte(this.m_client.LocaleID);

                                    Random random = new Random();
                                    byte[] MAC = new byte[6];
                                    random.NextBytes(MAC);
                                    response.WriteByteArray(MAC);
                                    response.Lock();

                                    return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(response));
                                }


                                if (packet.Opcode == 0xA103) {
                                    var sucess = packet.ReadByte();
                                    if (sucess == 1) {
                                        Packet response = new Packet(0x7007);
                                        response.WriteByte(2);
                                        response.Lock();

                                        return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(response));
                                    } else {
                                        this.m_client.CanAccountLogin = true;
                                    }
                                }

                                if (packet.Opcode == 0xB007) {
                                    byte type = packet.ReadByte();
                                    if (type == 2) {
                                        byte success = packet.ReadByte();
                                        if (success == 1) {

                                            this.m_client.CanCharacterSelection = true;

                                            byte characterCount = packet.ReadByte();
                                            if (characterCount > 0) {
                                                m_client.Characters.Clear();
                                                for (byte i = 0; i < characterCount; i++) {
                                                    m_client.Characters.Add(new Character(packet));
                                                }
                                            }
                                        }
                                    }
                                }

                                if (packet.Opcode == 0x3020) {
                                    Packet response = new Packet(0x3012);
                                    Packet response2 = new Packet(0x750E);

                                    return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(new List<Packet>() { response, response2 }));
                                }


                                if (packet.Opcode == 0x34A6) {
                                    this.m_client.CanClientlessSwitchToClient = true;
                                    this.ClientComponent.OnCharacterStatusChanged?.Invoke(client, CharacterStatusType.SPAWN_SUCCESS);
                                }

                                if (packet.Opcode == 0xB04C) {
                                    byte sucess = packet.ReadByte();
                                    if (sucess == 1) {

                                        Console.WriteLine("Waiting for Return Scroll");

                                        //Program.main.ReturnScrollStarted();
                                    } else {
                                        Console.WriteLine("ReturnScroll faild, please check Slot (NO INSTANT-RETURN-SCROLLS SIPPORTED YET)");
                                    }
                                }


                            }
                        } else {

                            //Teleport Request
                            if (packet.Opcode == 0x30D2) {

                                //Wait for Client
                                //!!! Create Callback here, because this would freeze the thread resulting in connection lost when taking too long!!!
                                //while (m_ClientWaitingForData == false && m_ClientWatingForFinish == false) {
                                //    System.Threading.Thread.Sleep(1);
                                //}

                                //Client is ready
                                if (this.m_client.IsWaitingForData) {
                                    Packet respone = new Packet(0x34B6);

                                    this.m_client.IsWatingForFinish = true;

                                    Console.WriteLine("Waiting for Teleport to finish");

                                    return new PacketResult(PacketOperationType.RESPONSE, new PacketResult.PacketResponseResultInfo(respone));
                                }
                            }

                            if (packet.Opcode == 0x34A5) {
                                if (this.m_client.IsWatingForFinish) {

                                    this.m_client.CanSwitchClient = false;
                                    this.m_client.IsClientless = false;

                                    Console.WriteLine("Sucessfully switched");
                                }
                            }

                        }


                    }

                    return new PacketResult(PacketOperationType.NOTHING);
                });

            }
        }

        private PacketResult GetPacketResultOnClientPacketReceived(SROClient client, SROPacket packet) {

            //For ClientlessSwitcher
            if (this.m_client.CanSwitchClient) {
                #region Fake Client

                #region 0x2001
                if (packet.Opcode == 0x2001) {

                    //[S -> C][2001][16 bytes]
                    //0D 00 47 61 74 65 77 61 79 53 65 72 76 65 72 00   ..GatewayServer.
                    Packet response = new Packet(0x2001);
                    if (!this.m_client.IsConnectedToAgent) {
                        response.WriteAscii("GatewayServer");
                    } else {
                        response.WriteAscii("AgentServer");
                        this.m_client.IsConnectedToAgent = false;
                    }
                    response.WriteByte(0); //Client-Connection
                    response.Lock();

                    client.SendPacket(response);

                    response = new Packet(0x2005, false, true);
                    response.WriteByte(0x01);
                    response.WriteByte(0x00);
                    response.WriteByte(0x01);
                    response.WriteByte(0xBA);
                    response.WriteByte(0x02);
                    response.WriteByte(0x05);
                    response.WriteByte(0x00);
                    response.WriteByte(0x00);
                    response.WriteByte(0x00);
                    response.WriteByte(0x02);
                    response.Lock();

                    client.SendPacket(response);

                    response = new Packet(0x6005, false, true);
                    response.WriteByte(0x03);
                    response.WriteByte(0x00);
                    response.WriteByte(0x02);
                    response.WriteByte(0x00);
                    response.WriteByte(0x02);
                    response.Lock();

                    client.SendPacket(response);
                }
                #endregion

                #region 0x6100
                if (packet.Opcode == 0x6100) {
                    byte local = packet.ReadByte();
                    string identity = packet.ReadAscii();
                    uint version = packet.ReadUInt();

                    //S->P:A100 Data:01
                    Packet response = new Packet(0xA100, false, true);

                    if (local != this.m_client.LocaleID) {
                        response.WriteByte(0x02);
                        response.WriteByte(0x01); //(C4)                   
                    } else if (identity != "SR_Client") {
                        response.WriteByte(0x02); 
                        response.WriteByte(0x03); //(C4)                 
                    } else if (version != this.m_client.VersionID) {
                        response.WriteByte(0x02);
                        response.WriteByte(0x02);
                    } else {
                        response.WriteByte(0x01); //Success
                    }

                    response.Lock();
                    client.SendPacket(response);
                }
                #endregion

                #region 0x6101

                if (packet.Opcode == 0x6101 && this.m_client.IsConnectedToAgent == false) {
                    Packet response = new Packet(0xA102);
                    response.WriteByte(0x01); //Success
                    response.WriteUInt(uint.MaxValue); //SessionID
                    response.WriteAscii("127.0.0.1"); //AgentIP
                    response.WriteUShort(this.m_client.LocalPort);
                    response.Lock();

                    this.m_client.IsConnectedToAgent = true;
                    client.SendPacket(response);
                }

                #endregion

                #region 0x6103

                if (packet.Opcode == 0x6103) {
                    uint sessionID = packet.ReadUInt();
                    string username = packet.ReadAscii();
                    string password = packet.ReadAscii();
                    byte local = packet.ReadByte();
                    //byte[] mac = packet.ReadByteArray(6); //No need

                    Packet response = new Packet(0xA103);
                    if (sessionID != uint.MaxValue) {
                        response.WriteByte(0x02);
                        response.WriteByte(0x02);
                    } else if (!string.IsNullOrEmpty(username)) {
                        response.WriteByte(0x02);
                        response.WriteByte(0x02);
                    } else if (!string.IsNullOrEmpty(password)) {
                        response.WriteByte(0x02);
                        response.WriteByte(0x02);
                    } else if (local != this.m_client.LocaleID) {
                        response.WriteByte(0x02);
                        response.WriteByte(0x02);
                    } else {
                        response.WriteByte(0x01); //Success
                    }
                    response.Lock();
                    client.SendPacket(response);
                }

                #endregion

                #region 0x7007

                if (packet.Opcode == 0x7007) {
                    byte type = packet.ReadByte();
                    if (type == 0x02) {
                        Packet responseEndCS = new Packet(0xB001);
                        responseEndCS.WriteByte(0x01);

                        Packet responseInitLoad = new Packet(0x34A5);

                        client.SendPacket(responseEndCS);
                        client.SendPacket(responseInitLoad);
                        this.m_client.IsWaitingForData = true;
                    }
                }

                #endregion

                #endregion
            } else {
                //Not sure why but after clientless->client the clients preferes to send 0x6103 twice.
                if (packet.Opcode == 0x6103) {
                    if (this.m_client.AgentLoginFixCounter > 0) {
                        return new PacketResult(PacketOperationType.IGNORE);
                    }
                    this.m_client.AgentLoginFixCounter++;
                }

                if (packet.Opcode == 0x6102) {
                    this.m_client.AgentLoginFixCounter = 0;
                }

                return new PacketResult(PacketOperationType.NOTHING);

                //if (this.m_client.IsConnectedToAgent) {
                //    this.m_agentComponent.
                //    m_agentSocket.Send(packet);
                //} else {
                //    m_gatewaySocket.Send(packet);
                //}
            }

            return new PacketResult(PacketOperationType.NOTHING);
        }

        public void StartClient(bool useClientless) {
            if (useClientless) {
                m_loader.StartClient("", 5, 5);
            }
        }

        public bool DOLogin(IPEndPoint bind, uint localeID, uint versionID, uint shardID, string captcha, Account account, string CharName) {

            //if(bind == null)
            //if (Gateway != null || Agent != null)
            //    return;

            return true;
        }

        public void Connect() {
            this.m_gatewayComponent.DOBind(delegate (bool success, BindErrorType bindError) {
                if (success) {
                    Console.WriteLine("[GATEWAY LISTENER] PROXY bind SUCCESS.");
                   

                    if (this.ClientComponent.IsClientless) {

                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Clientless auto-login is not supported yet.");
                        Console.ResetColor();

                        //Thread t = new Thread(new ThreadStart(this.StartFakeClient));
                        //t.Start();

                        //Console.WriteLine("[GATEWAY LISTENER] Clientless socket connection in progress...");
                    } else {
                        Console.WriteLine("[GATEWAY LISTENER] Client socket connection is waiting...");
                    }

                } else {
                    Console.WriteLine("[GATEWAY LISTENER] PROXY bind FAILED -- Reason : " + bindError);
                }
            });

           
        }

        SROServiceContext fakeClientService;

        private void StartFakeClient() {
            Thread.Sleep(1000);

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //socket.Blocking = false;
            //socket.NoDelay = true;

            SROClient fakeClient = new SROClient(socket);

            fakeClientService = new SROServiceContext(fakeClient, this.m_gatewayComponent);

            fakeClientService.DOBind(delegate (SROClient client, SocketError socketError) {

                if (socketError == SocketError.Success) {
                    Console.WriteLine("[GATEWAY LISTENER] Clientless socket bind SUCCESS");

                    ConnectFakeClient();

                } else {
                    Console.WriteLine("[GATEWAY LISTENER] Clientless socket bind failed to host. -- Reason : " + socketError);
                }


            });

        }

        private void ConnectFakeClient() {
            fakeClientService.DOConnect(this.ClientComponent.LocalGatewayEndPoint, false);
        }

    }
}
