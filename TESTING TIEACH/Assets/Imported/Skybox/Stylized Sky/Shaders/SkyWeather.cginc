float _WeatherDaylight, _WeatherTime, _ConstellationIndex;
float SkyHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float Puff(float2 p,float2 c,float2 r) { return 1-smoothstep(0.86,1.04,length((p-c)/r)); }
float SkyLink(float2 p,float2 a,float2 b) {
 float2 ab=b-a;
 float dist=length(p-a-ab*saturate(dot(p-a,ab)/dot(ab,ab)));
 float stroke=(1-smoothstep(0.0003,0.0006+max(fwidth(dist),0.0005),dist))*0.04;
 float starDist=min(length(p-a),length(p-b));
 return max(stroke,(1-smoothstep(0.002,0.003+max(fwidth(starDist),0.0006),starDist))*0.65);
}
float3 SkyWeather(float3 sky,float3 d) {
 float az=atan2(d.x,d.z), el=asin(clamp(d.y,-1.0,1.0));
 float2 uv=float2((az+UNITY_PI)/(2*UNITY_PI)*180,el*45);
 float2 cell=floor(uv);
 float seed=SkyHash(cell+67.1);
 float2 center=0.2+0.6*float2(SkyHash(cell+9.7),SkyHash(cell+23.8));
 float dist=length(frac(uv)-center);
 float stars=(1-smoothstep(0.025,0.045+clamp(length(fwidth(uv)),0.015,0.12),dist))*step(0.90,seed);
 stars*=lerp(0.15,0.38,seed)*(0.9+0.1*sin(_WeatherTime*2+seed*90));
 float2 p=float2(az-0.48,el-0.34);
 float pattern=0;
 if(_ConstellationIndex<0.5) {
 pattern=SkyLink(p,float2(-0.20,0.01),float2(-0.13,0.05));
 pattern=max(pattern,SkyLink(p,float2(-0.13,0.05),float2(-0.06,0.03)));
 pattern=max(pattern,SkyLink(p,float2(-0.06,0.03),float2(0,0)));
 pattern=max(pattern,SkyLink(p,float2(0,0),float2(0.13,0.02)));
 pattern=max(pattern,SkyLink(p,float2(0.13,0.02),float2(0.11,-0.08)));
 pattern=max(pattern,SkyLink(p,float2(0.11,-0.08),float2(0.02,-0.08)));
 pattern=max(pattern,SkyLink(p,float2(0.02,-0.08),float2(0,0)));
 } else if(_ConstellationIndex<1.5) {
 pattern=SkyLink(p,float2(-0.14,0.06),float2(-0.07,-0.04));
 pattern=max(pattern,SkyLink(p,float2(-0.07,-0.04),float2(0,0.04)));
 pattern=max(pattern,SkyLink(p,float2(0,0.04),float2(0.07,-0.04)));
 pattern=max(pattern,SkyLink(p,float2(0.07,-0.04),float2(0.14,0.06)));
 } else {
 pattern=SkyLink(p,float2(0,0.12),float2(-0.09,0.02));
 pattern=max(pattern,SkyLink(p,float2(-0.09,0.02),float2(0,-0.07)));
 pattern=max(pattern,SkyLink(p,float2(0,-0.07),float2(0.08,0.02)));
 pattern=max(pattern,SkyLink(p,float2(0.08,0.02),float2(0,0.12)));
 pattern=max(pattern,SkyLink(p,float2(0,-0.07),float2(0.045,-0.13)));
 }
 sky+=max(stars,pattern)*smoothstep(0.03,0.14,d.y)*smoothstep(0.35,0.95,1-_WeatherDaylight)*float3(0.83,0.90,1);
 float coverage=0,shade=0;
 [unroll] for(int n=0;n<12;n++) {
 float angle=n*(2*UNITY_PI/12)-_WeatherTime*0.32;
 float altitude=0.10+SkyHash(float2(n,7))*0.35;
 float size=lerp(0.7,1.2,SkyHash(float2(n,19)));
 float2 q=float2(atan2(sin(az-angle),cos(az-angle))*cos(altitude),el-altitude)/size;
 float c=Puff(q,float2(0,-0.015),float2(0.15,0.038));
 c=max(c,Puff(q,float2(-0.09,0.015),float2(0.063,0.047)));
 c=max(c,Puff(q,float2(-0.035,0.045),float2(0.066,0.069)));
 c=max(c,Puff(q,float2(0.035,0.03),float2(0.073,0.060)));
 c=max(c,Puff(q,float2(0.105,0.005),float2(0.058,0.044)));
 if(c>coverage) { coverage=c; shade=saturate((q.y+0.05)/0.15); }
 }
 return lerp(sky,lerp(float3(0.72,0.78,0.85),float3(1,0.99,0.96),shade),coverage*smoothstep(0.02,0.09,d.y)*_WeatherDaylight*0.96);
}
