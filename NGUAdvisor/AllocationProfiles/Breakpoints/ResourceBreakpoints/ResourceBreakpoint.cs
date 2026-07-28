using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public enum ResourceType
    {
        Energy,
        Magic,
        R3,
        Consumable
    }

    public abstract class ResourceBreakpoint
    {
        protected static readonly Character _character = Main.Character;

        private double? CapPercent { get; set; }

        protected int Index { get; private set; }

        protected ResourceType Type { get; private set; }

        public bool IsCap { get; private set; }

        protected long MaxAllocation { get; private set; }

        private long GetIdleResourceAmount()
        {
            switch (Type)
            {
                case ResourceType.Energy:
                    return _character.idleEnergy;
                case ResourceType.Magic:
                    return _character.magic.idleMagic;
                case ResourceType.R3:
                    return _character.res3.idleRes3;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private long GetMaxResourceAmount()
        {
            if (!IsCap)
                return GetIdleResourceAmount();

            switch (Type)
            {
                case ResourceType.Energy:
                    return _character.curEnergy;
                case ResourceType.Magic:
                    return _character.magic.curMagic;
                case ResourceType.R3:
                    return _character.res3.curRes3;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void UpdateMaxAllocation(int prioCount = 1)
        {
            long capMax = GetMaxResourceAmount();
            if (CapPercent.HasValue)
                capMax = (long)Math.Ceiling(capMax * CapPercent.Value);
            else if (!IsCap)
                capMax = capMax / prioCount + Math.Sign(capMax % prioCount);
            MaxAllocation = Math.Min(capMax, GetIdleResourceAmount());
        }

        public bool IsValid() => CorrectResourceType() && Unlocked() && !TargetMet();

        protected abstract bool Unlocked();

        protected abstract bool TargetMet();

        public abstract bool Allocate();

        protected abstract bool CorrectResourceType();

        protected void SetInput(long val)
        {
            _character.energyMagicPanel.energyRequested.text = val.ToString();
            _character.energyMagicPanel.validateInput();
        }

        // No rebirthTime parameter: the rebirth deadline is a property of the RUN, not of a parsed
        // breakpoint. It was never passed by any caller (BR and BestAug both read 0 and their
        // "will it finish before the rebirth" guards silently never fired); both now read it live
        // from Main.Profile.NextRebirthTargetSeconds().
        public static IEnumerable<ResourceBreakpoint> ParseBreakpointArray(JSONNode node, ResourceType type)
        {
            var prios = node.AsArray.Children.Select(x => x.Value.ToUpper());
            foreach (var prio in prios)
            {
                double? cap;
                int index;
                var temp = prio;
                if (temp.Contains(":"))
                {
                    var split = prio.Split(':');
                    temp = split[0];
                    var success = int.TryParse(split[1], out var tempCap);
                    if (!success)
                        cap = 100;
                    else if (tempCap > 100)
                        cap = 100;
                    else if (tempCap < 0)
                        cap = 0;
                    else
                        cap = tempCap;
                    cap /= 100;
                }
                else
                {
                    cap = null;
                }


                if (temp.Contains("-"))
                {
                    var split = temp.Split('-');
                    temp = split[0];
                    var success = int.TryParse(split[1], out index);
                    if (!success)
                        index = -1;
                }
                else
                {
                    index = 0;
                }

                if (temp.StartsWith("NGU") || temp.StartsWith("CAPNGU"))
                {
                    yield return new NGUBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = temp.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.Contains("ALLNGU"))
                {
                    var top = 0;
                    if (type == ResourceType.Energy)
                        top = 9;
                    else if (type == ResourceType.Magic)
                        top = 7;
                    for (var i = 0; i < top; i++)
                    {
                        yield return new NGUBP
                        {
                            CapPercent = cap,
                            Index = i,
                            IsCap = temp.Contains("CAP"),
                            Type = type
                        };
                    }
                }
                else if (temp.StartsWith("CAPAT") || temp.StartsWith("AT"))
                {
                    yield return new AdvancedTrainingBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = temp.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("AUG") || temp.StartsWith("CAPAUG"))
                {
                    yield return new AugmentBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = temp.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("BESTAUG") || temp.StartsWith("CAPBESTAUG"))
                {
                    yield return new BestAug
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = temp.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("BT") || temp.StartsWith("CAPBT"))
                {
                    yield return new BasicTrainingBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = temp.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("HACK") || temp.StartsWith("CAPHACK"))
                {
                    yield return new HackBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = temp.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("WAN") || temp.StartsWith("CAPWAN"))
                {
                    yield return new WandoosBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = temp.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("ALLBT") || temp.StartsWith("CAPALLBT"))
                {
                    var top = _character.settings.syncTraining ? 6 : 12;
                    for (var i = 0; i < top; i++)
                    {
                        yield return new BasicTrainingBP
                        {
                            CapPercent = cap,
                            Index = i,
                            IsCap = temp.Contains("CAP"),
                            Type = type
                        };
                    }
                }
                else if (temp.StartsWith("BR") || temp.StartsWith("CAPBR"))
                {
                    yield return new BR
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = prio.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("TM") || temp.StartsWith("CAPTM"))
                {
                    yield return new TimeMachineBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = prio.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("RIT") || temp.StartsWith("CAPRIT"))
                {
                    yield return new RitualBP
                    {
                        CapPercent = cap,
                        Index = index,
                        IsCap = prio.Contains("CAP"),
                        Type = type
                    };
                }
                else if (temp.StartsWith("ALLAT") || temp.StartsWith("CAPALLAT"))
                {
                    for (var i = 0; i < 5; i++)
                    {
                        yield return new AdvancedTrainingBP
                        {
                            CapPercent = cap,
                            Index = i,
                            IsCap = temp.Contains("CAP"),
                            Type = type,
                            GroupSpread = true   // even waterfill across the 5 slots (see AdvancedTrainingBP)
                        };
                    }
                }
                else if (temp.StartsWith("ALLHACK") || temp.StartsWith("CAPALLHACK"))
                {
                    // Hacks with target first
                    foreach (var hack in _character.hacks.hacks.Where(x => x.target > 0))
                    {
                        yield return new HackBP
                        {
                            CapPercent = cap,
                            Index = _character.hacks.hacks.IndexOf(hack),
                            IsCap = temp.Contains("CAP"),
                            Type = type
                        };
                    }
                    foreach (var hack in _character.hacks.hacks.Where(x => x.target == 0))
                    {
                        yield return new HackBP
                        {
                            CapPercent = cap,
                            Index = _character.hacks.hacks.IndexOf(hack),
                            IsCap = temp.Contains("CAP"),
                            Type = type
                        };
                    }
                }
                else
                {
                    yield return null;
                }
            }
        }
    }
}
