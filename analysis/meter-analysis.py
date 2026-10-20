"""Audio-based conventional meter estimates, not recovery of a unique score.
Never changes the existing numerator/pulse count. Confidence is a heuristic score,
not a calibrated probability. Ambiguous/sparse evidence keeps denominator=None.
"""
import numpy as np

def onset_envelope(audio,sr):
    from scipy.signal import stft
    from scipy.ndimage import maximum_filter1d
    import soxr
    mono=audio.mean(axis=1) if audio.ndim==2 else audio
    y=soxr.resample(mono,sr,22050)
    _,times,z=stft(y,fs=22050,nperseg=512,noverlap=402,boundary='zeros')
    mag=np.log1p(20*np.abs(z))
    flux=np.maximum(0,np.diff(mag,axis=1,prepend=mag[:,:1])).mean(axis=0)
    flux=maximum_filter1d(flux,size=5)
    return times,flux

def estimate_denominators(audio,sr,bars,beats):
    times,flux=onset_envelope(audio,sr)
    beats=np.asarray(beats)
    strength=float(np.quantile(flux,.95)) if len(flux) else 0
    def at(t):
        i=int(np.searchsorted(times,t));a=max(0,i-8);b=min(len(flux),i+9)
        return float(np.max(flux[a:b])) if b>a else 0
    features=[]
    for bar in bars:
        count=bar.get('count');start,end=bar['start'],bar['end']
        pulse=beats[(beats>=start-.06)&(beats<end-.06)]
        if not count or len(pulse)!=count or strength<1e-7:
            features.append(None);continue
        edges=np.r_[pulse,end];gaps=np.diff(edges);dt=float(np.median(gaps))
        accents=np.array([at(t) for t in pulse]);scale=max(1e-9,float(np.mean(accents)))
        half=np.array([at(t+d*.5) for t,d in zip(pulse,gaps)])
        thirds=np.array([.5*(at(t+d/3)+at(t+2*d/3)) for t,d in zip(pulse,gaps)])
        off=np.array([at(t+d*.19) for t,d in zip(pulse,gaps)])
        binary=float(np.clip((np.mean(half)-np.mean(off))/scale,0,1))
        ternary=float(np.clip((np.mean(thirds)-np.mean(off))/scale,0,1))
        peak=float(np.max(accents))
        # Compound/additive candidates require an actual accent grouping pattern.
        partitions={6:[[3,3]],9:[[3,3,3]],12:[[3,3,3,3]],5:[[2,3],[3,2]],7:[[2,2,3],[2,3,2],[3,2,2]],8:[[3,3,2],[3,2,3],[2,3,3]]}.get(count,[])
        group=0
        for sizes in partitions:
            starts=np.cumsum([0]+sizes[:-1]);weak=[i for i in range(count) if i not in starts]
            if weak:group=max(group,float(np.clip((np.mean(accents[starts])-np.mean(accents[weak]))/max(1e-9,peak),0,1)))
        features.append({'binary':binary,'ternary':ternary,'group':group,'dt':dt,'energy':peak/strength})
    for i,bar in enumerate(bars):
        bar['denominator']=None;bar['meterConfidence']=0;bar['meterMethod']='audio-convention-v1'
        if features[i] is None:continue
        # Pool only consecutive same-numerator neighbours, never across a meter change.
        indices=[i]
        for direction in [-1,1]:
            for distance in [1,2]:
                j=i+direction*distance
                if not 0<=j<len(bars) or bars[j].get('count')!=bar.get('count'):break
                if features[j] is not None:indices.append(j)
        f={key:float(np.median([features[j][key] for j in indices])) for key in features[i]}
        binary,ternary,group,dt=f['binary'],f['ternary'],f['group'],f['dt']
        candidates={2:0.,4:0.,8:0.,16:0.}
        if f['energy']>.08:
            # Conventional simple notation: binary subdivision, not pulse speed alone.
            if binary>.12 and binary>ternary*1.25:
                candidates[4]=min(.9,.52+.36*binary) if .25<=dt<=1.1 else .40
                if dt>1.1:candidates[2]=min(.78,.52+.3*binary)
            # Conventional grouped eighth/sixteenth notation requires group accents.
            if group>.24 and ternary<max(.18,binary*1.4):
                if .20<=dt<=.48:candidates[8]=min(.9,.58+.38*group)
                elif .09<=dt<.20:candidates[16]=min(.84,.54+.38*group)
        ranked=sorted(candidates.items(),key=lambda x:x[1],reverse=True)
        winner,score=ranked[0];margin=score-ranked[1][1]
        bar['meterEvidence']={k:round(v,3) for k,v in f.items()}
        bar['meterCandidates']={str(k):round(v,3) for k,v in candidates.items()}
        if score>=.60 and margin>=.12:
            bar['denominator']=winner;bar['meterConfidence']=round(score,3)
    # Corroborate borderline evidence across several consecutive, compatible bars.
    for i,bar in enumerate(bars):
        if bar['denominator'] is not None or features[i] is None:continue
        scores=bar.get('meterCandidates',{})
        if not scores:continue
        winner=max(scores,key=scores.get)
        if scores[winner]<.55:continue
        peers=[]
        for direction in [-1,1]:
            for distance in range(1,5):
                j=i+direction*distance
                if not 0<=j<len(bars) or bars[j].get('count')!=bar.get('count'):break
                if features[j] is None:break
                if abs(features[j]['dt']/features[i]['dt']-1)>.2:break
                candidate=bars[j].get('meterCandidates',{})
                if candidate and max(candidate.values())>=.55:peers.append(candidate)
        agreeing=[p for p in peers if max(p,key=p.get)==winner]
        if len(agreeing)>=2 and len(agreeing)>=.8*len(peers):
            bar['denominator']=int(winner)
            bar['meterConfidence']=round(min(.7,float(np.mean([scores[winner]]+[p[winner] for p in agreeing]))+.05),3)
            bar['meterRegionalSupport']=len(agreeing)+1
    return bars
