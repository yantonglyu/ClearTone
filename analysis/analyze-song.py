"""Local one-shot timeline analysis. Input is a temporary licensed audio file.
No network credentials are read. Only compact numerical features are retained.
"""
import json, sys, subprocess, hashlib
from pathlib import Path
import numpy as np


def structure(beats, downbeats, duration):
    beats=np.asarray(beats,dtype=float)
    downbeats=np.asarray(downbeats,dtype=float)
    bars=[]
    for start,end in zip(downbeats[:-1],downbeats[1:]):
        inside=beats[(beats>=start-.06)&(beats<end-.06)]
        count=len(inside)
        gaps=np.diff(np.r_[inside,end])
        # A denominator cannot be inferred from pulse/downbeat timing alone.
        # Preserve local changes instead of forcing a song-wide 4/4 assumption.
        regular=len(gaps)>1 and np.std(gaps)/max(.01,np.mean(gaps))<.22
        bars.append({'start':round(float(start),3),'end':round(float(end),3),
                     'count':int(count) if regular and 2<=count<=12 else None})
    tempo=[]
    # Eight-second estimates limit jitter, retaining sustained tempo changes.
    for start in np.arange(0,duration,8):
        local=beats[(beats>=max(0,start-4))&(beats<min(duration,start+12))]
        gaps=np.diff(local)
        gaps=gaps[(gaps>.2)&(gaps<2)]
        bpm=None
        if len(gaps)>=4:
            med=np.median(gaps)
            good=gaps[np.abs(gaps-med)<med*.2]
            if len(good)>=len(gaps)*.7:bpm=int(round(60/np.median(good)))
        if tempo and bpm and tempo[-1]['bpm'] and abs(bpm-tempo[-1]['bpm'])<=2:bpm=tempo[-1]['bpm']
        tempo.append({'start':float(start),'end':min(float(start+8),duration),'bpm':bpm})
    supported=[segment['bpm'] for segment in tempo if segment['bpm']]
    if supported:
        anchor=float(max(supported,key=supported.count))
        for segment in tempo:
            bpm=segment['bpm']
            if bpm:
                # Keep the pulse convention consistent through half/double-time ambiguity.
                choices=[bpm/2,bpm,bpm*2]
                aligned=min(choices,key=lambda x:abs(np.log2(x/anchor)))
                if abs(aligned-anchor)/anchor<.12:segment['bpm']=int(round(aligned))
    return bars,tempo


def vocal_pitch(vocal, sr):
    import librosa, soxr
    y=soxr.resample(vocal,sr,12000).astype(np.float32)
    hop=600; width=2048
    f0=librosa.yin(y,fmin=65,fmax=1400,sr=12000,frame_length=width,hop_length=hop)
    padded=np.pad(y,(width//2,width//2))
    out=[]
    for i,hz in enumerate(f0):
        frame=padded[i*hop:i*hop+width]
        rms=float(np.sqrt(np.mean(frame**2)))
        lag=int(round(12000/hz))
        left,right=frame[:-lag],frame[lag:]
        correlation=float(np.dot(left,right)/max(1e-9,np.linalg.norm(left)*np.linalg.norm(right)))
        out.append(round(float(hz),1) if rms>.0025 and correlation>.80 and 66<hz<1390 else None)
    # Quantize stable notes, rejecting brief crossings from vibrato and noisy consonants.
    result=[]; current=None; candidate=None; count=0; silence=0
    for i,hz in enumerate(out):
        nearby=[x for x in out[max(0,i-1):i+2] if x]
        if hz and nearby:
            midi=69+12*np.log2(float(np.median(nearby))/440)
            note=int(round(midi)); silence=0
            if current is not None and abs(midi-current)<.72:note=current
            if note==candidate:count+=1
            else:candidate=note;count=1
            if count>=3:current=note
        else:
            silence+=1;candidate=None;count=0
            if silence>=8:current=None
        result.append(round(float(440*2**((current-69)/12)),1) if current is not None else None)
    return result


def analyze(source, target, expected, seed=None):
    import torch, imageio_ffmpeg
    from demucs.pretrained import get_model
    from demucs.apply import apply_model
    from beat_this.inference import Audio2Beats
    torch.set_num_threads(4)
    torch.manual_seed(0)
    # Decode directly, avoiding torchaudio codec/plugin changes.
    command=[imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-i',source,'-t','1201','-f','f32le','-ac','2','-ar','44100','pipe:1']
    decoded=subprocess.run(command,check=True,capture_output=True,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0)).stdout
    audio=np.frombuffer(decoded,dtype='<f4').reshape(-1,2).copy()
    duration=len(audio)/44100
    if duration<10 or duration>1200 or abs(duration-expected)>3:raise ValueError('Duration mismatch or unsupported length')
    import importlib.util
    spec=importlib.util.spec_from_file_location('meter_analysis',Path(__file__).with_name('meter-analysis.py'))
    meter=importlib.util.module_from_spec(spec);spec.loader.exec_module(meter)
    if seed and Path(seed).is_file():
        old=json.loads(Path(seed).read_text(encoding='utf-8-sig'))
        if old.get('sourceHash')==hashlib.sha256(Path(source).read_bytes()).hexdigest() and old.get('version')==3:
            old['bars']=meter.estimate_denominators(audio,44100,old['bars'],old['beats'])
            old['version']=4;old['models']['meter']='audio-convention-v1'
            Path(target).write_text(json.dumps(old,separators=(',',':')),encoding='utf-8')
            return
    result={'version':4,'models':{'vocals':'htdemucs','beats':'beat-this-final0','pitch':'stable-vocal-v3','meter':'audio-convention-v1'},
            'sourceHash':hashlib.sha256(Path(source).read_bytes()).hexdigest(),'duration':duration,
            'pitchStep':.05,'pitch':[None]*int(np.ceil(duration/.05)),
            'beats':[],'downbeats':[],'bars':[],'tempo':[],'pitchRanges':[]}
    def publish():
        temporary=Path(target+'.partial-writing')
        temporary.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
        temporary.replace(Path(target+'.partial'))
    def beats_pass():
        print('beats',flush=True)
        model=Audio2Beats(checkpoint_path='final0',device='cpu',dbn=False)
        beats,downbeats=model(audio,44100)
        result['bars'],result['tempo']=structure(beats,downbeats,duration)
        meter.estimate_denominators(audio,44100,result['bars'],beats)
        result['beats']=[round(float(x),3) for x in beats]
        result['downbeats']=[round(float(x),3) for x in downbeats]
        publish()
    print('vocals',flush=True)
    separator=get_model('htdemucs').eval()
    # Twenty-second cores with two-second overlap context avoid cut-edge artifacts.
    # Prioritize where playback will be when cold-start inference completes.
    # The opening eight seconds are filled afterwards, without shifting timestamps.
    windows=([(8,min(duration,20))]+[(float(s),min(duration,float(s)+20)) for s in np.arange(20,duration,20)]+[(0,min(duration,8))]) if duration>20 else [(0,duration)]
    for index,(start,end) in enumerate(windows):
        context=1 if index==0 else 2
        left=max(0,start-context);right=min(duration,end+context)
        wave=torch.from_numpy(audio[int(round(left*44100)):int(round(right*44100))].T.copy())
        reference=wave.mean(0);mean=reference.mean();std=reference.std().clamp_min(1e-6)
        with torch.inference_mode():
            separated=apply_model(separator,((wave-mean)/std)[None],device='cpu',shifts=0,split=True,overlap=.1,progress=False,num_workers=0)[0]
        vocal=(separated[separator.sources.index('vocals')]*std+mean).mean(0).numpy()
        pitches=vocal_pitch(vocal,44100)
        offset=int(round((start-left)/.05));begin=int(round(start/.05));count=min(int(round((end-start)/.05)),len(result['pitch'])-begin)
        result['pitch'][begin:begin+count]=pitches[offset:offset+count]
        result['pitchRanges'].append([float(start),float(end)])
        publish();print('vocals',flush=True)
        if index==1:beats_pass()
    if not result['beats']:beats_pass()
    Path(target).write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')

if __name__=='__main__':analyze(sys.argv[1],sys.argv[2],float(sys.argv[3]),sys.argv[4] if len(sys.argv)>4 else None)
