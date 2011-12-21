﻿//  Copyright 2011 Marc Fletcher, Matthew Dean
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetworkCommsDotNet;
using ProtoBuf;
using System.Threading.Tasks;
using SerializerBase;

namespace DistributedFileSystem
{
    /// <summary>
    /// Object passed around between peers when keeping everyone updated.
    /// </summary>
    [ProtoContract]
    public class PeerChunkAvailabilityUpdate
    {
        [ProtoMember(1)]
        public long ItemCheckSum { get; private set; }
        [ProtoMember(2)]
        public ChunkFlags ChunkFlags { get; private set; }

        private PeerChunkAvailabilityUpdate() { }

        public PeerChunkAvailabilityUpdate(long itemCheckSum, ChunkFlags chunkFlags)
        {
            this.ItemCheckSum = itemCheckSum;
            this.ChunkFlags = chunkFlags;
        }
    }

    public static class LongBitCount
    {
        /// <summary>
        /// Returns the number of bits set to 1 in a ulong
        /// </summary>
        /// <param name="longToCount"></param>
        /// <returns></returns>
        public static byte CountBits(ulong inputLong)
        {
            byte currentCount = 0;
            long tmp = 0;

            int lowerLong = (int)(inputLong & 0x00000000FFFFFFFF);
            tmp = (lowerLong - ((lowerLong >> 1) & 3681400539) - ((lowerLong >> 2) & 1227133513));
            currentCount += (byte)(((tmp + (tmp >> 3)) & 3340530119) % 63);

            int upperLong = (int)(inputLong >> 32);
            tmp = (upperLong - ((upperLong >> 1) & 3681400539) - ((upperLong >> 2) & 1227133513));
            currentCount += (byte)(((tmp + (tmp >> 3)) & 3340530119) % 63);

            return currentCount;
        }
    }

    /// <summary>
    /// Provides 256 length bit flag 
    /// </summary>
    [ProtoContract]
    public class ChunkFlags
    {
        [ProtoMember(1)]
        ulong flags0;
        [ProtoMember(2)]
        ulong flags1;
        [ProtoMember(3)]
        ulong flags2;
        [ProtoMember(4)]
        ulong flags3;

        private ChunkFlags() { }

        /// <summary>
        /// Initialises the chunkflags. The initial state is generally 0 or totalNumChunks
        /// </summary>
        /// <param name="intialState"></param>
        public ChunkFlags(byte intialState)
        {
            flags0 = 0;
            flags1 = 0;
            flags2 = 0;
            flags3 = 0;

            for (byte i = 0; i < intialState; i++)
                SetFlag(i);
        }

        /// <summary>
        /// Returns true if the provided chunk is available. Zero indexed from least significant bit.
        /// </summary>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public bool FlagSet(byte chunkIndex)
        {
            if (chunkIndex > 255 || chunkIndex < 0)
                throw new Exception("Chunk index must be between 0 and 255 inclusive.");

            if (chunkIndex >= 192)
                return ((flags3 & (1UL << (chunkIndex - 192))) > 0);
            else if (chunkIndex >= 128)
                return ((flags2 & (1UL << (chunkIndex - 128))) > 0);
            else if (chunkIndex >= 64)
                return ((flags1 & (1UL << (chunkIndex - 64))) > 0);
            else
                return ((flags0 & (1UL << chunkIndex)) > 0);
        }

        /// <summary>
        /// Sets the bit flag to 1 which corresponds with the provided chunk index. Zero indexed from least significant bit.
        /// </summary>
        /// <param name="chunkIndex"></param>
        public void SetFlag(byte chunkIndex, bool state = true)
        {
            if (chunkIndex > 255 || chunkIndex < 0)
                throw new Exception("Chunk index must be between 0 and 255 inclusive.");

            //If we are setting a flag from 1 to 0
            if (state)
            {
                if (chunkIndex >= 192)
                    flags3 |= (1UL << (chunkIndex - 192));
                else if (chunkIndex >= 128)
                    flags2 |= (1UL << (chunkIndex - 128));
                else if (chunkIndex >= 64)
                    flags1 |= (1UL << (chunkIndex - 64));
                else
                    flags0 |= (1UL << chunkIndex);
            }
            else
            {
                if (chunkIndex >= 192)
                    flags3 &= ~(1UL << (chunkIndex - 192));
                else if (chunkIndex >= 128)
                    flags2 &= ~(1UL << (chunkIndex - 128));
                else if (chunkIndex >= 64)
                    flags1 &= ~(1UL << (chunkIndex - 64));
                else
                    flags0 &= ~(1UL << chunkIndex);
            }
        }

        /// <summary>
        /// Updates local chunk flags with those provided by using an OR operator. This ensures we never disable chunks which are already set available.
        /// </summary>
        /// <param name="latestChunkFlags"></param>
        public void UpdateFlags(ChunkFlags latestChunkFlags)
        {
            flags0 |= latestChunkFlags.flags0;
            flags1 |= latestChunkFlags.flags1;
            flags2 |= latestChunkFlags.flags2;
            flags3 |= latestChunkFlags.flags3;
        }

        /// <summary>
        /// Returns true if all bit flags upto the provided uptoChunkIndexInclusive are set to true
        /// </summary>
        /// <param name="uptoChunkIndexInclusive"></param>
        public bool AllFlagsSet(byte uptoChunkIndexInclusive)
        {
            for (byte i = 0; i < uptoChunkIndexInclusive; i++)
                if (!FlagSet(i))
                    return false;

            return true;
        }

        public byte NumCompletedChunks()
        {
            return (byte)(LongBitCount.CountBits(flags0) + LongBitCount.CountBits(flags1) + LongBitCount.CountBits(flags2) + LongBitCount.CountBits(flags3));
        }

        public void ClearAllFlags()
        {
            flags0 = 0;
            flags1 = 0;
            flags2 = 0;
            flags3 = 0;
        }
    }

    /// <summary>
    /// Wrapper class for ChunkFlags which allows us to include a little more information about a peer
    /// </summary>
    [ProtoContract]
    public class PeerAvailabilityInfo
    {
        /// <summary>
        /// The chunk availability for this peer.
        /// </summary>
        [ProtoMember(1)]
        public ChunkFlags PeerChunkFlags { get; private set; }

        /// <summary>
        /// For now the only extra info we want. A superPeer is generally busier network wise and should be contacted last for data.
        /// </summary>
        [ProtoMember(2)]
        public bool SuperPeer { get; private set; }

        /// <summary>
        /// Used to maintain peer busy status
        /// </summary>
        public DateTime PeerBusyAnnounce { get; private set; }
        public bool PeerBusy { get; private set; }

        private PeerAvailabilityInfo() { }

        public PeerAvailabilityInfo(ChunkFlags peerChunkFlags, bool superPeer)
        {
            this.PeerChunkFlags = peerChunkFlags;
            this.SuperPeer = superPeer;
        }

        public void SetPeerBusy()
        {
            PeerBusyAnnounce = DateTime.Now;
            PeerBusy = true;
        }

        public void ClearBusy()
        {
            PeerBusy = false;
        }
    }

    [ProtoContract]
    public class SwarmChunkAvailability : IThreadSafeSerialise
    {
        [ProtoMember(1)]
        Dictionary<string, PeerAvailabilityInfo> peerAvailabilityByNetworkIdentifierDict;
        [ProtoMember(2)]
        Dictionary<string, ConnectionInfo> peerNetworkIdentifierToConnectionInfo;

        object peerLocker = new object();

        /// <summary>
        /// Blank constructor used for serailisation
        /// </summary>
        private SwarmChunkAvailability() { }

        /// <summary>
        /// Creates a new instance of SwarmChunkAvailability for the superNode
        /// </summary>
        /// <param name="sourceNetworkIdentifier"></param>
        /// <param name="sourceConnectionInfo"></param>
        public SwarmChunkAvailability(ConnectionInfo sourceConnectionInfo, byte totalNumChunks)
        {
            //When initialising the chunk availability we add the starting source in the intialisation
            peerAvailabilityByNetworkIdentifierDict = new Dictionary<string, PeerAvailabilityInfo>() { { sourceConnectionInfo.NetworkIdentifier, new PeerAvailabilityInfo(new ChunkFlags(totalNumChunks), true) } };
            peerNetworkIdentifierToConnectionInfo = new Dictionary<string, ConnectionInfo>() { { sourceConnectionInfo.NetworkIdentifier, sourceConnectionInfo } };

#if logging
            DFS.logger.Debug("New SwarmChunkAvailability by " + sourceNetworkIdentifier + ".");
#endif
        }

        /// <summary>
        /// Builds a dictionary of chunk availability throughout the current swarm for chunks we don't have locally. Keys are chunkIndex, peer network identifier, and peer total chunk count
        /// </summary>
        /// <param name="chunksRequired"></param>
        /// <returns></returns>
        public Dictionary<byte, Dictionary<ConnectionInfo, PeerAvailabilityInfo>> CachedNonLocalChunkExistences(byte totalChunksInItem, out Dictionary<ConnectionInfo, PeerAvailabilityInfo> nonLocalPeerAvailability)
        {
            lock (peerLocker)
            {
                Dictionary<byte, Dictionary<ConnectionInfo, PeerAvailabilityInfo>> chunkExistence = new Dictionary<byte, Dictionary<ConnectionInfo, PeerAvailabilityInfo>>();
                nonLocalPeerAvailability = new Dictionary<ConnectionInfo, PeerAvailabilityInfo>();

                //We add an entry to the dictionary for every chunk we do not yet have
                for (byte i = 0; i < totalChunksInItem; i++)
                    if (!PeerHasChunk(NetworkComms.NetworkNodeIdentifier, i))
                        chunkExistence.Add(i, new Dictionary<ConnectionInfo, PeerAvailabilityInfo>());

                //Now for each peer we know about we add them to the list if they have a chunck of interest
                foreach (var peer in peerAvailabilityByNetworkIdentifierDict)
                {
                    //This is the only place we clear a peers busy status
                    if (peer.Value.PeerBusy && (DateTime.Now - peer.Value.PeerBusyAnnounce).TotalMilliseconds > DFS.PeerBusyTimeoutMS)
                        peer.Value.ClearBusy();

                    if (PeerContactAllowed(NetworkIdentifierToConnectionInfo(peer.Key).ClientIP, peer.Value.SuperPeer))
                    {
                        //For this peer for every chunk we are looking for
                        for (byte i = 0; i < totalChunksInItem; i++)
                        {
                            if (chunkExistence.ContainsKey(i) && peer.Value.PeerChunkFlags.FlagSet(i))
                            {
                                chunkExistence[i].Add(NetworkIdentifierToConnectionInfo(peer.Key), peer.Value);

                                if (!nonLocalPeerAvailability.ContainsKey(NetworkIdentifierToConnectionInfo(peer.Key)))
                                    nonLocalPeerAvailability.Add(NetworkIdentifierToConnectionInfo(peer.Key), peer.Value);
                            }
                        }
                    }
                }

#if logging
                DFS.logger.Debug("... returned latest CachedChunkSwarmExistences.");
#endif

                return chunkExistence;
            }
        }

        public void SetPeerBusy(ShortGuid networkIdentifier)
        {
            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SetPeerBusy();
            }
        }

        public bool PeerBusy(ShortGuid networkIdentifier)
        {
            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].PeerBusy;
            }

            return false;
        }

        /// <summary>
        /// Returns true if a peer with the provided networkIdentifier exists in the swarm for this item
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public bool PeerExistsInSwarm(ShortGuid networkIdentifier)
        {
            lock (peerLocker)
                return peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier);
        }

        /// <summary>
        /// Returns true if the specified peer has the provided chunkIndex.
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public bool PeerHasChunk(ShortGuid networkIdentifier, byte chunkIndex)
        {
            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].PeerChunkFlags.FlagSet(chunkIndex);
                else
                    throw new Exception("No peer was found in peerChunksByNetworkIdentifierDict with the provided networkIdentifier.");
            }
        }

        /// <summary>
        /// Returns true if a peer has a complete copy of a file
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public bool PeerIsComplete(ShortGuid networkIdentifier, byte totalNumChunks)
        {
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    throw new Exception("networkIdentifier provided does not exist in peerChunksByNetworkIdentifierDict. Check with PeerExistsInSwarm before calling this method.");

                return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].PeerChunkFlags.AllFlagsSet(totalNumChunks);
            }
        }

        /// <summary>
        /// Returns true if the specified peer is a super peer
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public bool PeerIsSuperPeer(ShortGuid networkIdentifier)
        {
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                    throw new Exception("networkIdentifier provided does not exist in peerChunksByNetworkIdentifierDict. Check with PeerExistsInSwarm before calling this method.");

                return peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer;
            }
        }

        /// <summary>
        /// Converts a provided network identifier into an ip address. There is no guarantee the ip is connectable.
        /// </summary>
        /// <param name="networkIdentifier"></param>
        /// <returns></returns>
        public ConnectionInfo NetworkIdentifierToConnectionInfo(ShortGuid networkIdentifier)
        {
            lock (peerLocker)
            {
                if (peerNetworkIdentifierToConnectionInfo.ContainsKey(networkIdentifier))
                    return peerNetworkIdentifierToConnectionInfo[networkIdentifier];
                else
                    throw new Exception("Unable to convert network identifier to ip as it's entry was not found in peerNetworkIdentifierToIP dictionary.");
            }
        }

        /// <summary>
        /// Delets the knowledge of a peer from our local swarm chunk availability
        /// </summary>
        /// <param name="networkIdentifier"></param>
        public void RemovePeerFromSwarm(ShortGuid networkIdentifier, bool forceRemove = false)
        {
            lock (peerLocker)
            {
                //We only remove the peer if we have more than one and it is not a super peer
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(networkIdentifier))
                {
                    if (forceRemove || (peerAvailabilityByNetworkIdentifierDict.Count > 1 && !peerAvailabilityByNetworkIdentifierDict[networkIdentifier].SuperPeer))
                    {
                        peerAvailabilityByNetworkIdentifierDict.Remove(networkIdentifier);

                        if (peerNetworkIdentifierToConnectionInfo.ContainsKey(networkIdentifier))
                            peerNetworkIdentifierToConnectionInfo.Remove(networkIdentifier);

#if logging
                DFS.logger.Debug("... removed " + networkIdentifier + " from item swarm.");
#endif

                        //Console.WriteLine("... removing peer " + networkIdentifier + ".");
                    }
                }
            }
        }

        /// <summary>
        /// Adds or updates a peer to the local availability list. Usefull for when a peer informs us of an updated availability.
        /// </summary>
        /// <param name="peerNetworkIdentifier"></param>
        /// <param name="latestChunkFlags"></param>
        public void AddOrUpdateCachedPeerChunkFlags(string peerNetworkIdentifier, ChunkFlags latestChunkFlags)
        {
            lock (peerLocker)
            {
                if (peerAvailabilityByNetworkIdentifierDict.ContainsKey(peerNetworkIdentifier))
                    peerAvailabilityByNetworkIdentifierDict[peerNetworkIdentifier].PeerChunkFlags.UpdateFlags(latestChunkFlags);
                else
                {
                    //We also need to add the ip address. This should not fail as if we are calling this method locally we should have the relevant connection
                    if (!peerNetworkIdentifierToConnectionInfo.ContainsKey(peerNetworkIdentifier))
                        peerNetworkIdentifierToConnectionInfo.Add(peerNetworkIdentifier, NetworkComms.ConnectionIdToConnectionInfo(new ShortGuid(peerNetworkIdentifier)));

                    peerAvailabilityByNetworkIdentifierDict.Add(peerNetworkIdentifier, new PeerAvailabilityInfo(latestChunkFlags, false));
                }

#if logging
                DFS.logger.Debug("... updated peer chunk availability flags for " + peerNetworkIdentifier + ".");
#endif
            }
        }

        /// <summary>
        /// Sets our local availability
        /// </summary>
        /// <param name="chunkIndex"></param>
        public void SetLocalChunkFlag(byte chunkIndex, bool setAvailable)
        {
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(NetworkComms.NetworkNodeIdentifier))
                    throw new Exception("Local peer not located in peerChunkAvailabity for this item.");

                peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkNodeIdentifier].PeerChunkFlags.SetFlag(chunkIndex, setAvailable);
            }
        }

        /// <summary>
        /// Update the chunk availability by contacting all existing peers. If a cascade depth greater than 1 is provided will also contact each peers peers.
        /// </summary>
        /// <param name="cascadeDepth"></param>
        public void UpdatePeerAvailability(long itemCheckSum, int cascadeDepth, int responseTimeoutMS = 1000)
        {
            if (cascadeDepth > 1)
                throw new NotImplementedException("A cascading update has not yet been implemented. Only direct connection to original peers is.");

            string[] peerKeys;
            lock (peerLocker)
                peerKeys = peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();

            List<Task> updateTasks = new List<Task>();

            //Contact all known peers and request an availability update
            foreach (ShortGuid peerIdentifierOuter in peerKeys)
            {
                ShortGuid peerIdentifier = peerIdentifierOuter;
                //Removed tasks as this wants to run in the same thread as the originating call
                updateTasks.Add(Task.Factory.StartNew(new Action(() =>
                {
                    try
                    {
                        string clientIP; int clientPort; bool superPeer;
                        lock (peerLocker)
                        {
                            clientIP = peerNetworkIdentifierToConnectionInfo[peerIdentifier].ClientIP;
                            superPeer = peerAvailabilityByNetworkIdentifierDict[peerIdentifier].SuperPeer;
                            clientPort = peerNetworkIdentifierToConnectionInfo[peerIdentifier].ClientPort;
                        }

                        //We don't want to contact ourselves and for now that includes anything having the same ip as us
                        if (peerIdentifier != NetworkComms.NetworkNodeIdentifier && PeerContactAllowed(clientIP, superPeer))
                        {
                            if (NetworkComms.ConnectionExists(peerIdentifier))
                                NetworkComms.SendObject("DFS_ChunkAvailabilityRequest", peerIdentifier, false, itemCheckSum);
                            else
                                NetworkComms.SendObject("DFS_ChunkAvailabilityRequest", clientIP, clientPort, false, itemCheckSum);
                        }
                    }
                    catch (CommsException)
                    {
                        //If a peer has disconnected we just remove them from the list
                        RemovePeerFromSwarm(peerIdentifier);
                        //NetworkComms.LogError(ex, "UpdatePeerChunkAvailabilityCommsError");
                    }
                    catch (KeyNotFoundException)
                    {
                        //This exception will get thrown if we try to access a peers connecitonInfo from peerNetworkIdentifierToConnectionInfo 
                        //but it has been removed since we accessed the peerKeys at the start of this method
                        //We could probably be a bit more carefull with how we maintain these references but we either catch the
                        //exception here or networkcomms will throw one when we try to connect to an old peer.
                        RemovePeerFromSwarm(peerIdentifier);
                    }
                    catch (Exception ex)
                    {
                        NetworkComms.LogError(ex, "UpdatePeerChunkAvailabilityError");
                    }
                })));
            }

            //All peers have upto 1 second to sort out their shit otherwise we move on without them responding.
            Task.WaitAll(updateTasks.ToArray(), responseTimeoutMS);
        }

        /// <summary>
        /// Metric used to determine the health of a chunk and whether swarm will benefit from a broadcasted update.
        /// </summary>
        /// <param name="chunkIndex"></param>
        /// <returns></returns>
        public double ChunkHealthMetric(byte chunkIndex, byte totalNumChunks)
        {
            lock (peerLocker)
            {
                //How many peers have this chunk
                int chunkExistenceCount = (from current in peerAvailabilityByNetworkIdentifierDict where current.Value.PeerChunkFlags.FlagSet(chunkIndex) select current.Key).Count();

                //How many peers are currently assembling the file
                int totalNumIncompletePeers = peerAvailabilityByNetworkIdentifierDict.Count(entry => !entry.Value.PeerChunkFlags.AllFlagsSet(totalNumChunks));

                //=((1.5*($A3-0.5))/B$2)

                //return (1.5*(double)chunkExistenceCount) / (double)totalNumIncompletePeers;
                return (1.5 * ((double)chunkExistenceCount - 0.5)) / (double)totalNumIncompletePeers;
            }
        }

        /// <summary>
        /// Records a chunk as available for the local peer
        /// </summary>
        /// <param name="chunkIndex"></param>
        public void RecordLocalChunkCompletion(byte chunkIndex)
        {
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(NetworkComms.NetworkNodeIdentifier))
                    throw new Exception("Local peer not located in peerChunkAvailabity for this item.");

                peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkNodeIdentifier].PeerChunkFlags.SetFlag(chunkIndex);
            }
        }

        /// <summary>
        /// Updates all peers in the swarm that we have updated a chunk
        /// </summary>
        /// <param name="itemCheckSum"></param>
        /// <param name="chunkIndex"></param>
        public void BroadcastLocalAvailability(long itemCheckSum)
        {
            //Console.WriteLine("Updating swarm availability.");

            string[] peerKeys;
            ChunkFlags localChunkFlags;
            lock (peerLocker)
            {
                peerKeys = peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();
                localChunkFlags = peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkNodeIdentifier].PeerChunkFlags;
            }

            foreach (ShortGuid peerIdentifier in peerKeys)
            {
                try
                {
                    string clientIP; int clientPort; bool superPeer;
                    lock (peerLocker)
                    {
                        clientIP = peerNetworkIdentifierToConnectionInfo[peerIdentifier].ClientIP;
                        superPeer = peerAvailabilityByNetworkIdentifierDict[peerIdentifier].SuperPeer;
                        clientPort = peerNetworkIdentifierToConnectionInfo[peerIdentifier].ClientPort;
                    }

                    //We don't want to contact ourselves
                    if (peerIdentifier != NetworkComms.NetworkNodeIdentifier && PeerContactAllowed(clientIP, superPeer))
                    {
                        //For each peer that is not us we send our latest availability.
                        if (NetworkComms.ConnectionExists(peerIdentifier))
                            NetworkComms.SendObject("DFS_PeerChunkAvailabilityUpdate", peerIdentifier, false, new PeerChunkAvailabilityUpdate(itemCheckSum, localChunkFlags));
                        else
                            NetworkComms.SendObject("DFS_PeerChunkAvailabilityUpdate", clientIP, clientPort, false, new PeerChunkAvailabilityUpdate(itemCheckSum, localChunkFlags));
                    }
                }
                //We don't just want to catch comms exceptions because if a peer has potentially disconnected we may get a KeyNotFoundException here
                catch (Exception)
                {
                    //If a peer has disconnected we just remove them from the list
                    RemovePeerFromSwarm(peerIdentifier);
                }
            }
        }

        private bool PeerContactAllowed(string peerIP, bool superPeer)
        {
            //We always allow super peers
            //If this is not a super peer and we have set the allowedPeerIPs then we check in there
            bool peerAllowed;
            if (!superPeer && (DFS.allowedPeerIPs.Count > 0 || DFS.disallowedPeerIPs.Count > 0))
            {
                if (DFS.allowedPeerIPs.Count > 0)
                    peerAllowed = DFS.allowedPeerIPs.Contains(peerIP);
                else
                    peerAllowed = !DFS.disallowedPeerIPs.Contains(peerIP);
            }
            else
                peerAllowed = true;

            return peerAllowed;
        }

        public ChunkFlags PeerChunkAvailability(ShortGuid peerIdentifier)
        {
            lock (peerLocker)
            {
                if (PeerExistsInSwarm(peerIdentifier))
                    return peerAvailabilityByNetworkIdentifierDict[peerIdentifier].PeerChunkFlags;
                else
                    throw new Exception("A peer with the provided peerIdentifier does not exist in this swarm.");
            }
        }

        /// <summary>
        /// Broadcast to all known peers that the local DFS is removing the specified item
        /// </summary>
        /// <param name="itemCheckSum"></param>
        public void BroadcastItemRemoval(long itemCheckSum)
        {
            string[] peerKeys;
            lock (peerLocker)
            {
                if (!peerAvailabilityByNetworkIdentifierDict.ContainsKey(NetworkComms.NetworkNodeIdentifier))
                    throw new Exception("Local peer not located in peerChunkAvailabity for this item.");

                peerKeys = peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();
            }

            foreach (ShortGuid outerPeerIdentifier in peerKeys)
            {
                ShortGuid peerIdentifier = outerPeerIdentifier;

                //Do this with a task so that it does not block
                Task.Factory.StartNew(new Action(() =>
                {
                    try
                    {
                        string clientIP; int clientPort; bool superPeer;
                        lock (peerLocker)
                        {
                            clientIP = peerNetworkIdentifierToConnectionInfo[peerIdentifier].ClientIP;
                            superPeer = peerAvailabilityByNetworkIdentifierDict[peerIdentifier].SuperPeer;
                            clientPort = peerNetworkIdentifierToConnectionInfo[peerIdentifier].ClientPort;
                        }

                        //We don't want to contact ourselves
                        if (peerIdentifier != NetworkComms.NetworkNodeIdentifier && PeerContactAllowed(clientIP, superPeer))
                        {

                            //For each peer that is not us we send our latest availability.
                            if (NetworkComms.ConnectionExists(peerIdentifier))
                                NetworkComms.SendObject("DFS_ItemRemovedLocallyUpdate", peerIdentifier, false, itemCheckSum);
                            else
                                NetworkComms.SendObject("DFS_ItemRemovedLocallyUpdate", clientIP, clientPort, false, itemCheckSum);

                        }
                    }
                    catch (CommsException)
                    {
                        RemovePeerFromSwarm(peerIdentifier);
                    }
                    catch (Exception e)
                    {
                        RemovePeerFromSwarm(peerIdentifier);
                        NetworkComms.LogError(e, "BroadcastItemRemovalError");
                    }
                }));
            }
        }

        public int NumPeersInSwarm()
        {
            lock (peerLocker)
                return peerAvailabilityByNetworkIdentifierDict.Count;
        }

        /// <summary>
        /// Returns an array containing the network identifiers of every peer in this swarm
        /// </summary>
        /// <returns></returns>
        public string[] AllPeerIdentifiers()
        {
            lock (peerLocker)
                return peerAvailabilityByNetworkIdentifierDict.Keys.ToArray();
        }

        public void ClearAllLocalAvailabilityFlags()
        {
            lock (peerLocker)
                peerAvailabilityByNetworkIdentifierDict[NetworkComms.NetworkNodeIdentifier].PeerChunkFlags.ClearAllFlags();
        }

        #region IThreadSafeSerialise Members

        public byte[] ThreadSafeSerialise()
        {
            lock (peerLocker)
                return NetworkComms.DefaultSerializer.SerialiseDataObject<SwarmChunkAvailability>(this, NetworkComms.DefaultCompressor);
        }

        #endregion
    }
}