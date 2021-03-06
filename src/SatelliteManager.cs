﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace RemoteTech {
    public class SatelliteManager : IEnumerable<VesselSatellite>, IDisposable {
        public delegate void SatelliteHandler(VesselSatellite satellite);

        public event SatelliteHandler Registered;
        public event SatelliteHandler Unregistered;

        public int Count { get { return mSatelliteCache.Count; } }
        public VesselSatellite this[Guid g] { get { return this.For(g); } }
        public VesselSatellite this[Vessel v] { get { return this.For(v.id); } }

        private readonly Dictionary<Guid, List<ISignalProcessor>> mLoadedSpuCache =
            new Dictionary<Guid, List<ISignalProcessor>>();
        private readonly Dictionary<Guid, VesselSatellite> mSatelliteCache =
            new Dictionary<Guid, VesselSatellite>();
        private readonly RTCore mCore;

        public SatelliteManager(RTCore core) {
            mCore = core;
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselDestroy.Add(OnVesselDestroy);
            GameEvents.onVesselGoOnRails.Add(OnVesselOnRails);
        }

        /// <summary>
        /// Registers a signal processor for the vessel.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        /// <param name="spu">The signal processor.</param>
        /// <returns>Guid key under which the signal processor was registered.</returns>
        public Guid Register(Vessel vessel, ISignalProcessor spu) {
            Guid key = vessel.protoVessel.vesselID;
            RTUtil.Log("SatelliteManager: Register {0}, {1} ", key, spu.Vessel.vesselName);
            // Unregister any unloaded proto-satellites if necessary.
            if (!mLoadedSpuCache.ContainsKey(key)) {
                UnregisterProto(vessel.id);
                mLoadedSpuCache[key] = new List<ISignalProcessor>();
            }
            // Add if non duplicate
            ISignalProcessor instance = mLoadedSpuCache[key].Find(x => x == spu);
            if (instance == null) {
                mLoadedSpuCache[key].Add(spu);
                // Create a new satellite if it's the only loaded signal processor.
                if(mLoadedSpuCache[key].Count == 1) {
                    mSatelliteCache[key] = new VesselSatellite(mLoadedSpuCache[key]);
                    OnRegister(mSatelliteCache[key]);
                }
            }

            return key;
        }

        /// <summary>
        /// Unregisters the specified signal processor.
        /// </summary>
        /// <param name="key">The key the signal processor was registered under.</param>
        /// <param name="spu">The signal processor.</param>
        public void Unregister(Guid key, ISignalProcessor spu) {
            RTUtil.Log("SatelliteManager: Unregister {0}", key);
            // Return if nothing to unregister.
            if (!mLoadedSpuCache.ContainsKey(key)) return;
            // Find instance of the signal processor.
            int instance_id = mLoadedSpuCache[key].FindIndex(x => x == spu);
            if (instance_id != -1) {
                // Remove satellite if no signal processors remain.
                if (mLoadedSpuCache[key].Count == 1) {
                    if (mSatelliteCache.ContainsKey(key)) {
                        VesselSatellite sat = mSatelliteCache[key];
                        OnUnregister(sat);
                        mSatelliteCache.Remove(key);
                    }
                    mLoadedSpuCache[key].RemoveAt(instance_id);
                    mLoadedSpuCache.Remove(key);
                    RegisterProto(spu.Vessel);
                } else {
                    mLoadedSpuCache[key].RemoveAt(instance_id);
                }
            }
        }

        /// <summary>
        /// Registers a protosatellite compiled from the unloaded vessel data.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        public void RegisterProto(Vessel vessel) {
            Guid key = vessel.protoVessel.vesselID;
            RTUtil.Log("SatelliteManager: RegisterProto {0} ", vessel.vesselName);
            // Return if there are still signal processors loaded.
            if (mLoadedSpuCache.ContainsKey(vessel.id))
                return;
            
            ISignalProcessor spu = vessel.GetSignalProcessor();
            if (spu != null) {
                List<ISignalProcessor> protos = new List<ISignalProcessor>();
                protos.Add(spu);
                mSatelliteCache[key] = new VesselSatellite(protos);
                OnRegister(mSatelliteCache[key]);
            }
        }

        /// <summary>
        /// Unregisters the protosatellite which was compiled from the unloaded vessel data.
        /// </summary>
        /// <param name="vessel">The vessel.</param>
        public void UnregisterProto(Guid key) {
            RTUtil.Log("SatelliteManager: UnregisterProto {0}", key);
            // Return if there are still signal processors loaded.
            if (mLoadedSpuCache.ContainsKey(key)) {
                return;
            }
            // Unregister satellite if it exists.
            if (mSatelliteCache.ContainsKey(key)) {
                OnUnregister(mSatelliteCache[key]);
                mSatelliteCache.Remove(key);
            }
        }

        private VesselSatellite For(Guid key) {
            return mSatelliteCache.ContainsKey(key) ? mSatelliteCache[key] : null;
        }

        public IEnumerable<ISatellite> FindCommandStations() {
            return mSatelliteCache.Values.Where(vs => vs.CommandStation).Cast<ISatellite>();
        }

        private void OnVesselOnRails(Vessel v) {
            if (v.parts.Count == 0) {
                RegisterProto(v);
            }
        }

        private void OnVesselCreate(Vessel v) {
            RTUtil.Log("SatelliteManager: OnVesselCreate {0}", v.vesselName);
        }

        private void OnVesselDestroy(Vessel v) {
            RTUtil.Log("SatelliteManager: OnVesselDestroy {0}", v.vesselName);
            UnregisterProto(v.id);
        }

        private void OnRegister(VesselSatellite vs) {
            RTUtil.Log("SatelliteManager: Successful register {0}", vs.Name);
            if (Registered != null) {
                Registered(vs);
            }
        }

        private void OnUnregister(VesselSatellite vs) {
            RTUtil.Log("SatelliteManager: Successful unregister {0}", vs.Name);
            if (Unregistered != null) {
                Unregistered(vs);
            }
            vs.Dispose();
        }

        public void Dispose() {
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselDestroy.Remove(OnVesselDestroy);
            GameEvents.onVesselGoOnRails.Remove(OnVesselOnRails);
        }

        public IEnumerator<VesselSatellite> GetEnumerator() {
            return mSatelliteCache.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }

    public static partial class RTUtil {
        public static bool IsSignalProcessor(this ProtoPartModuleSnapshot ppms) {
            return ppms.GetBool("IsRTSignalProcessor");

        }

        public static bool IsSignalProcessor(this PartModule pm) {
            return pm.Fields.GetValue<bool>("IsRTSignalProcessor");
        }

        public static ISignalProcessor GetSignalProcessor(this Vessel v) {
            RTUtil.Log("RTUtil: HasSignalProcessor {0}", v.vesselName);
            if (v.loaded) {
                foreach (Part p in v.Parts) {
                    foreach (PartModule pm in p.Modules) {
                        if (pm.IsSignalProcessor()) {
                            return pm as ISignalProcessor;
                        }
                    }
                }

            } else {
                foreach (ProtoPartModuleSnapshot ppms in
                                    v.protoVessel.protoPartSnapshots.SelectMany(x => x.modules)) {
                    if (ppms.IsSignalProcessor()) {
                        return new ProtoSignalProcessor(ppms, v);
                    }
                }
            }
            return null;
        }

        public static bool IsCommandStation(this ProtoPartModuleSnapshot ppms) {
            return ppms.GetBool("IsRTCommandStation");
        }

        public static bool IsCommandStation(this PartModule pm) {
            return pm.Fields.GetValue<bool>("IsRTCommandStation");
        }

        public static bool HasCommandStation(this Vessel v) {
            Log("RTUtil: HasCommandStation {0}", v.vesselName);
            if (v.loaded) {
                return v.Parts.SelectMany<Part, PartModule>(p => p.Modules.Cast<PartModule>())
                              .Any(pm => pm.IsCommandStation());
            } else {
                return v.protoVessel.protoPartSnapshots.SelectMany(x => x.modules)
                                                       .Any(pm => pm.IsCommandStation());
            }
        }
    }
}
