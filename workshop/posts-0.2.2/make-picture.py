import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter
F=r"C:\Windows\Fonts"
src=r"E:\Project\Mods\Sea Power\SeaPowerSensorViews\workshop\screenshots\05-map-sensor-lines-coverage.jpg"
out=r"E:\Project\Mods\Sea Power\SeaPowerSensorViews\workshop\posts-0.2.2\zoom-fix.jpg"
W,H=1920,1080
bg=Image.open(src).convert("RGB")
bg=bg.resize((W,int(bg.height*W/bg.width)),Image.LANCZOS)
canvas=Image.new("RGB",(W,H),(11,16,22)); canvas.paste(bg,(0,(H-bg.height)//2))
# dark panel on the right with a soft edge
ov=Image.new("L",(W,H),0); d=ImageDraw.Draw(ov)
for x in range(W):
    a=0 if x<760 else min(235,int((x-760)/260*235))
    d.line([(x,0),(x,H)],fill=a)
panel=Image.new("RGB",(W,H),(11,16,22)); canvas=Image.composite(panel,canvas,ov)
dr=ImageDraw.Draw(canvas)
sb=lambda s:ImageFont.truetype(os.path.join(F,"seguisb.ttf"),s); rg=lambda s:ImageFont.truetype(os.path.join(F,"segoeui.ttf"),s)
CY=(96,226,236); WH=(250,252,255); GR=(170,178,188); RED=(214,92,80)
L=1080
x=L
for ch in "R.A.T.S.":
    dr.text((x,150),ch,font=sb(34),fill=CY,anchor="ls"); x+=sb(34).getlength(ch)+8
dr.text((L,250),"Sensor Views 0.2.2",font=sb(84),fill=WH,anchor="ls")
dr.text((L,322),"Zoom lag fixed",font=rg(56),fill=(214,218,223),anchor="ls")
def group(y,label,before_val,before_txt,after_val,after_txt):
    dr.text((L,y),label,font=sb(40),fill=WH,anchor="ls")
    scale=7.0  # px per fps
    by=y+22
    if before_val>0:
        dr.rectangle([L,by,L+before_val*scale,by+44],fill=RED)
        dr.text((L+before_val*scale+16,by+36),before_txt,font=rg(36),fill=GR,anchor="ls")
    else:
        dr.rectangle([L,by,L+10,by+44],fill=RED)
        dr.text((L+28,by+36),before_txt,font=sb(36),fill=RED,anchor="ls")
    ay=by+60
    dr.rectangle([L,ay,L+after_val*scale,ay+44],fill=CY)
    dr.text((L+after_val*scale+16,ay+36),after_txt,font=sb(36),fill=WH,anchor="ls")
group(450,"Zoom 13",61,"31–61 fps",73,"72–74 fps")
group(660,"Zoom 17",0,"game froze",75,"74–76 fps")
# legend
ly=860
dr.rectangle([L,ly-26,L+28,ly],fill=RED); dr.text((L+40,ly),"0.2.1",font=rg(32),fill=GR,anchor="ls")
dr.rectangle([L+170,ly-26,L+198,ly],fill=CY); dr.text((L+210,ly),"0.2.2",font=rg(32),fill=GR,anchor="ls")
dr.text((L,930),"Longest frame at zoom 13:  0.86 s  →  0.05 s",font=rg(36),fill=(214,218,223),anchor="ls")
dr.text((L,1010),"Sensor lines on · same mission before and after · Sea Power 0.8.7",font=rg(26),fill=(140,148,158),anchor="ls")
canvas.save(out,quality=90)
print(out)

