  /* ==========================================================================
     v5 — "Remastered": the black-hole boot screen, shot with a camera.

     Same idea and the same beats as the screen this replaces — the imp leans
     in, his arms rise out of the sticker's own bottom corners, one orb per
     saved-post pip spirals in, a filament ignites between the palms, the core
     punches open, he drags the disk wide, and the clap pulls the frame into the
     hole — but the scene now sits under a camera that pushes, dollies and
     punches on the hit, over a star field and two parallax dust layers, with
     swirl lines around the disk and an RGB-split ring and debris on impact.

     The beats are the concept gallery's, compressed 0.775x so the clap lands on
     the exact 3.55 s frame this variant has always used, and the rig is the
     app's own: sticker() for the card, armToHandAimed() for the arms,
     brandName()/lsBrandGlitch() for the wordmark.
     ========================================================================== */
  function v5(sp){
    const T1=6.8, u=++uid;
    const ORBS=[0.39,0.68,0.98,1.27], IGNITE=1.46, CUP=0.82, OPEN=1.40,
          SPIN=2.31, WARP=2.87, CLAP=3.55, COL=3.81, FA=3.55, FB=4.00;
    const HX=CX, HY=CY+8, NP=30;
    const f1=n=>n.toFixed(1), f2=n=>n.toFixed(2), f3=n=>n.toFixed(3);
    const lerp=(a,b,k)=>a+(b-a)*k;
    const easeIn=k=>k*k*k;
    const backK=k=>{const c=1.9;return 1+(c+1)*Math.pow(k-1,3)+c*Math.pow(k-1,2)};
    const TAU2=Math.PI*2;

    /* deterministic scatter for the star field - the same boot always looks alike */
    function scatter(n,seed,box){
      let s=seed>>>0, out=[];
      const rnd=()=>((s=(s*1664525+1013904223)>>>0)/4294967296);
      for(let i=0;i<n;i++) out.push({x:(rnd()-.5)*box.w, y:(rnd()-.5)*box.h, r:.35+rnd()*.85, a:.18+rnd()*.5, ph:rnd()*TAU2});
      return out;
    }
    const BX=W*1.5, BY=H*1.5;
    const SKY=scatter(30,1337,{w:BX,h:BY});
    const FDUST=scatter(9,2027,{w:BX,h:BY});
    const NDUST=scatter(7,7717,{w:BX,h:BY});
    const skyMarkup=SKY.map(s=>`<circle cx="${f1(s.x)}" cy="${f1(s.y)}" r="${f2(s.r)}" fill="#d8f1ff" opacity="${f3(s.a*.75)}"/>`).join('');

    function dust(pts,t,pull,op,soft){
      let s='';
      for(const p of pts){
        const a=Math.atan2(p.y,p.x)+t*.10+pull*1.9;
        const d=Math.hypot(p.x,p.y)*(1-pull*(.42+.5*p.r));
        const x=Math.cos(a)*d+Math.sin(t*.6+p.ph)*4;
        const y=Math.sin(a)*d*.92+Math.cos(t*.45+p.ph)*3;
        s+=`<circle cx="${f1(x)}" cy="${f1(y)}" r="${f2(p.r*soft)}" fill="url(#mote${u})" opacity="${f3(p.a*op*(1-pull*.35))}"/>`;
      }
      return s;
    }
    /* swirl around the hole: the frame reads as moving even when the hole breathes */
    function wind(t,k,n,rIn,rOut){
      if(k<=.01) return '';
      let s='';
      for(let i=0;i<n;i++){
        const a=(i/n)*TAU2+t*(.5+(i%5)*.14);
        const rr=lerp(rIn,rOut,((i*37)%100)/100);
        const len=(4+(i*13)%16)*k/rr;
        const x1=Math.cos(a)*rr, y1=Math.sin(a)*rr*.62;
        const x2=Math.cos(a+len)*rr, y2=Math.sin(a+len)*rr*.62;
        s+=`<path d="M ${f1(x1)} ${f1(y1)} A ${f1(rr)} ${f1(rr*.62)} 0 0 1 ${f1(x2)} ${f1(y2)}" fill="none" stroke="${i%3?T().accent:T().accent2}" stroke-width="${f2(.45+(i%3)*.35)}" stroke-linecap="round" opacity="${f3(k*(.22+.58*((i*7)%10)/10))}"/>`;
      }
      return s;
    }
    function streaks(t,p,n,r0,r1){
      if(p<=.01||p>=1) return '';
      let s='';
      for(let i=0;i<n;i++){
        const a=(i/n)*TAU2+.19+Math.sin(i*4.7)*.06;
        const k=((i*29)%100)/100;
        const inner=r0+(r1-r0)*(.18+.55*k)*(1-p*.55);
        const outer=inner+(7+30*k)*(1-p);
        s+=`<line x1="${f1(Math.cos(a)*inner)}" y1="${f1(Math.sin(a)*inner*.78)}" x2="${f1(Math.cos(a)*outer)}" y2="${f1(Math.sin(a)*outer*.78)}" stroke="${i%3===0?'#ffffff':(i%2?T().accent:T().accent2)}" stroke-width="${f2(1+k*1.1)}" stroke-linecap="round" opacity="${f3((1-p)*(.3+.7*k))}"/>`;
      }
      return s;
    }
    function debris(p,n){
      if(p<=0||p>=1) return '';
      let s='';
      for(let i=0;i<n;i++){
        const a=(i*2.399963)%TAU2, sp=.55+((i*17)%23)/23*.95, d=p*(30+134*sp);
        s+=`<circle cx="${f1(Math.cos(a)*d)}" cy="${f1(Math.sin(a)*d*.78)}" r="${f2(Math.max(.2,(1-p)*(1+(i%3)*.7)))}" fill="${i%3?T().accent:T().accent2}" opacity="${f3((1-p)*.8)}"/>`;
      }
      return s;
    }
    function chroma(r,sw,op){
      if(op<=.01||r<=1) return '';
      return `<g opacity="${f3(op)}"><circle cx="-2" r="${f1(r)}" fill="none" stroke="#ff2d7a" stroke-width="${f2(sw)}" opacity=".85"/><circle cx="2" r="${f1(r)}" fill="none" stroke="#20d9ff" stroke-width="${f2(sw)}" opacity=".85"/></g>`;
    }

    let orbs='',parts='',disk='';
    for(let i=0;i<4;i++){
      const c=i%2?T().accent2:T().accent;
      orbs+=`<g id="ob${i}${u}"><circle r="4.6" fill="${c}"/><circle r="8" fill="none" stroke="${c}" stroke-width="1.1" opacity=".55"/><circle r="12" fill="none" stroke="${c}" stroke-width=".8" opacity=".22"/></g>`;
    }
    for(let i=0;i<NP;i++)parts+=`<line id="pt${i}${u}" x1="0" y1="0" x2="0" y2="0" stroke="${i%3?T().accent:T().accent2}" stroke-width="1.3" stroke-linecap="round" opacity="0"/>`;
    [[30,9,'2.6','11 7'],[48,14,'2.2','15 9'],[70,20,'1.8','21 12'],[94,27,'1.3','27 17']]
      .forEach((d,i)=>{disk+=`<ellipse id="dk${i}${u}" rx="${d[0]}" ry="${d[1]}" fill="none" stroke="url(#dsk${u})" stroke-width="${d[2]}" stroke-dasharray="${d[3]}" opacity="0"/>`;});

    sp.innerHTML=`<svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid meet">
      ${vignetteDef(u)}${glowDef(u)}
      <defs>
        <radialGradient id="coreG${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#000" stop-opacity="1"/>
          <stop offset="58%" stop-color="#000" stop-opacity=".97"/>
          <stop offset="100%" stop-color="#000" stop-opacity="0"/></radialGradient>
        <radialGradient id="haloG${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#000" stop-opacity="0"/>
          <stop offset="48%" stop-color="#000" stop-opacity="0"/>
          <stop offset="62%" stop-color="${T().accent}" stop-opacity=".16"/>
          <stop offset="74%" stop-color="#ffffff" stop-opacity=".48"/>
          <stop offset="86%" stop-color="${T().accent2}" stop-opacity=".24"/>
          <stop offset="100%" stop-color="${T().accent}" stop-opacity="0"/></radialGradient>
        <linearGradient id="dsk${u}" x1="0%" y1="0%" x2="100%" y2="0">
          <stop offset="0%" stop-color="${T().accent}"/><stop offset="45%" stop-color="#ffffff"/><stop offset="100%" stop-color="${T().accent2}"/></linearGradient>
        <radialGradient id="mote${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#eaf9ff" stop-opacity=".95"/>
          <stop offset="46%" stop-color="#cfefff" stop-opacity=".38"/>
          <stop offset="100%" stop-color="#bfe6ff" stop-opacity="0"/></radialGradient>
        <radialGradient id="wash${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="${T().accent}" stop-opacity=".30"/>
          <stop offset="46%" stop-color="${T().accent}" stop-opacity=".10"/>
          <stop offset="74%" stop-color="${T().accent2}" stop-opacity=".07"/>
          <stop offset="100%" stop-color="${T().accent2}" stop-opacity="0"/></radialGradient>
        <radialGradient id="sunG${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#ffffff" stop-opacity=".9"/>
          <stop offset="22%" stop-color="${T().accent}" stop-opacity=".34"/>
          <stop offset="55%" stop-color="${T().accent}" stop-opacity=".10"/>
          <stop offset="78%" stop-color="${T().accent2}" stop-opacity=".05"/>
          <stop offset="100%" stop-color="${T().accent2}" stop-opacity="0"/></radialGradient>
      </defs>
      <g id="cam${u}">
        <rect x="${-W}" y="${-H}" width="${W*3}" height="${H*3}" fill="${T().solid}"/>
        <g id="sky${u}">${skyMarkup}</g>
        <circle id="washEl${u}" cx="${HX}" cy="${HY}" r="0" fill="url(#wash${u})" opacity="0"/>
        <g id="farDust${u}"></g>
        <g id="orbs${u}">${orbs}</g>
        <g id="suck${u}">
          <g id="face${u}" transform="translate(${CX},${CY-24}) scale(1.0) translate(-80,-80)"></g>
          <g id="bname${u}">${brandName(u,CX,CY+66,13)}</g>
        </g>
        <g id="parts${u}">${parts}</g>
        <g id="hole${u}" transform="translate(${HX},${HY})">
          <circle id="halo${u}" r="0" fill="url(#haloG${u})" opacity="0" filter="url(#glow${u})"/>
          <g id="disk${u}" transform="rotate(-17)" filter="url(#glow${u})">${disk}
            <ellipse id="dkb${u}" rx="21" ry="6.5" fill="none" stroke="#ffffff" stroke-width="3.2" opacity="0"/></g>
          <circle id="coreC${u}" r="0" fill="url(#coreG${u})"/>
          <g id="lens${u}" opacity="0">
            <path d="M -27 -3 A 27 27 0 0 1 27 -3" fill="none" stroke="url(#dsk${u})" stroke-width="2.4"/>
            <path d="M -27 3 A 27 27 0 0 0 27 3" fill="none" stroke="url(#dsk${u})" stroke-width="2.4"/>
          </g>
          <circle id="ein${u}" r="0" fill="none" stroke="url(#dsk${u})" stroke-width="2" opacity="0"/>
        </g>
        <g id="windEl${u}" transform="translate(${HX},${HY})"></g>
        <g id="arms${u}"></g>
        <g id="lightFront${u}"></g>
        <g id="nearDust${u}"></g>
        <g id="shock${u}" transform="translate(${HX},${HY})"></g>
      </g>
      <rect id="flash${u}" x="-40" y="-40" width="${W+80}" height="${H+80}" fill="${T().accent}" opacity="0"/>
    </svg>`+CAP;

    const q=i=>sp.querySelector('#'+i+u);
    const E={cam:q('cam'),sky:q('sky'),wash:q('washEl'),farDust:q('farDust'),nearDust:q('nearDust'),
             orbs:[0,1,2,3].map(i=>q('ob'+i)),suck:q('suck'),face:q('face'),bname:q('bname'),
             pt:[...Array(NP)].map((_,i)=>q('pt'+i)),dk:[0,1,2,3].map(i=>q('dk'+i)),dkb:q('dkb'),
             core:q('coreC'),halo:q('halo'),disk:q('disk'),lens:q('lens'),ein:q('ein'),
             wind:q('windEl'),arms:q('arms'),lightFront:q('lightFront'),
             shock:q('shock'),flash:q('flash')};

    return {T:T1,update(t){
      const enter=win(t,0.02,0.40), intro=backK(enter);
      const cup=win(t,CUP,OPEN), openP=win(t,OPEN,SPIN), spinP=win(t,SPIN,WARP), warpP=win(t,WARP,CLAP);
      const clap=win(t,CLAP-0.18,CLAP), colP=win(t,CLAP,COL+0.06)*0.86;
      const impact=t-CLAP, hit=Math.max(0,1-Math.abs(impact)/0.30);

      /* the hole grows the whole way through - no plateau */
      const grow=.22+.62*(openP*.40+spinP*.32+warpP*.28);
      const size=Math.max(0,Math.min(1,Math.pow(grow,1.06))*(1-colP));

      /* ---- camera: slow push, a dolly through the disk, a punch on the clap */
      const push=1.002+.028*openP+.030*spinP+.058*Math.pow(warpP,1.45);
      const punch=impact>=0?.055*Math.max(0,1-impact/.42):0;
      const shakeAmp=cup*(1-clap)*.7+hit*4.2;
      const shx=(Math.sin(t*91)+.48*Math.sin(t*147+1.2))*shakeAmp;
      const shy=(Math.cos(t*103+.6)+.38*Math.sin(t*169))*shakeAmp;
      E.cam.setAttribute('transform',
        `translate(${f1(HX+shx)} ${f1(HY+shy)}) rotate(${f2(-0.8+1.2*openP+hit*1.7)}) scale(${f3(push+punch)}) translate(${f1(-HX)} ${f1(-HY)})`);

      /* ---- sky, key light, drifting dust */
      E.wash.setAttribute('r',f1(150+120*size));
      E.wash.setAttribute('opacity',f3(.30*Math.max(0,1-Math.abs(t-IGNITE)/.5)+.55*size));
      E.farDust.innerHTML=dust(FDUST,t,warpP*.55,.55,2.4);
      E.nearDust.innerHTML=dust(NDUST,t,warpP*.95,.42,1.7);

      /* ---- four orbs, one per boot step, each dragging a short light trail */
      E.orbs.forEach((el,i)=>{
        const p=(t-ORBS[i])/.86;
        if(p<0||p>1){el.setAttribute('opacity','0');return;}
        const ang=i*1.7+p*7.4+t*.10, rr=lerp(132,4,easeIn(p));
        el.setAttribute('transform',`translate(${f1(Math.cos(ang)*rr)},${f1(Math.sin(ang)*rr*.60)}) scale(${f2(1-p*.72)})`);
        el.setAttribute('opacity',f3(Math.min(1,p*6)*(1-p*p)));
      });

      /* ---- accretion disk */
      const growDisk=Math.min(1,size);
      E.halo.setAttribute('r',f1(104*size));
      E.halo.setAttribute('opacity',f3(size*(.82+.18*Math.sin(t*15))));
      E.disk.setAttribute('transform',`rotate(${f1(-17+t*46)})`);
      E.dk.forEach((el,i)=>{
        const on=win(t,OPEN+.08+i*.10,OPEN+.42+i*.10);
        el.setAttribute('opacity',f3(on*(.94+.06*Math.sin(t*12+i))*(1-colP)));
        el.setAttribute('transform',`scale(${f3(.22+growDisk*.80)})`);
        el.setAttribute('stroke-dashoffset',f1(-((t*92*(1+i*.38))%240)));
      });
      E.dkb.setAttribute('opacity',f3(size*(.82+.18*Math.sin(t*18))));
      E.dkb.setAttribute('stroke-width',f2(2.8+1.6*size));
      E.dkb.setAttribute('transform',`scale(${f3(.30+size*.92)})`);
      E.core.setAttribute('r',f1(62*size));
      E.lens.setAttribute('opacity',f3(size*(.82+.18*Math.sin(t*11))));
      E.lens.setAttribute('transform',`scale(${f3((62*size+3)/27)}) rotate(${f1(t*24)})`);
      E.ein.setAttribute('r',f1(70*size));
      E.ein.setAttribute('stroke-width',f2(2+1.5*size*(.55+.45*Math.sin(t*13))));
      E.ein.setAttribute('opacity',f3(.94*size));

      /* ---- matter spiralling in */
      E.pt.forEach((el,i)=>{
        const on=win(t,OPEN-.2,OPEN+.34);
        const ph=((t*.88)+(i/NP))%1, pp=ph*ph;
        const ang=(i/NP)*Math.PI*2+pp*10.2+t*.10;
        const rr=lerp(92,4,pp), tail=1.22+.22*size;
        el.setAttribute('x1',f1(Math.cos(ang)*rr));el.setAttribute('y1',f1(Math.sin(ang)*rr*.52));
        el.setAttribute('x2',f1(Math.cos(ang-.25)*rr*tail));el.setAttribute('y2',f1(Math.sin(ang-.25)*rr*tail*.52));
        el.setAttribute('stroke-width',f2(1.1+size*.8+(1-ph)*.35));
        el.setAttribute('opacity',f3(on*(1-colP)*Math.min(1,ph*5)*(1-pp*.3)*(.6+size*.4)));
      });

      /* ---- swirl outside the disk */
      E.wind.innerHTML=wind(t,Math.min(1,spinP*.55+warpP),16,26,86);

      /* ---- HE MAKES IT WITH HIS HANDS: the arms rise out of the card's own
         bottom corners, the palms cup nothing, the core lights between them,
         he drags it wide, then slams his hands together and it eats the frame. */
      const enterArm=win(t,.26,.92);
      let gap=70;
      gap=lerp(gap,44,cup);
      gap=lerp(gap,82,spinP);
      gap=lerp(gap,6,clap);
      const trem=Math.sin(t*15)*2.4*cup*(1-clap)+Math.sin(t*41)*.6*spinP;
      const hy=HY+3-3*cup+6*spinP+trem*.5;
      const hOp=enterArm*(1-Math.pow(warpP,3));
      /* the imp-outer-frame rect is x14 y14 w132 h132, so its bottom corners are
         14,146 / 146,146 in the card's own art space */
      const cnx=66, cny=66;
      E.arms.innerHTML=hOp>.02
        ? armToHandAimed(u,CX-cnx,CY-24+cny,CX-gap,hy,HX,HY,true ,hOp)
         +armToHandAimed(u,CX+cnx,CY-24+cny,CX+gap,hy,HX,HY,false,hOp)
        : '';

      /* ---- the filament between the palms before the core lights */
      if(cup>.18&&hOp>.02&&t<1.72){
        const spark=win(t,1.02,1.18)*(1-win(t,1.32,1.48));
        if(spark>.02){
          E.arms.innerHTML+=`<g opacity="${f3(spark)}" filter="url(#glow${u})">
            <line x1="${f1(CX-gap+7)}" y1="${f1(hy)}" x2="${f1(CX+gap-7)}" y2="${f1(hy)}" stroke="#ffffff" stroke-width="1.1" stroke-dasharray="2 5" stroke-dashoffset="${f1(-t*70)}"/>
            <line x1="${f1(CX-gap+7)}" y1="${f1(hy-3)}" x2="${f1(CX+gap-7)}" y2="${f1(hy+3)}" stroke="${T().accent}" stroke-width=".8" stroke-dasharray="1 8" stroke-dashoffset="${f1(t*50)}" opacity=".8"/>
            <circle cx="0" cy="0" r="${f2(Math.max(.4,1.4+1.6*Math.sin(t*34)))}" transform="translate(${f1(HX)} ${f1(hy)})" fill="#ffffff"/></g>`;
        }
      }

      /* ---- the hole washes light over the palms and the chin */
      E.lightFront.innerHTML=size>.03
        ? `<circle cx="${f1(HX)}" cy="${f1(HY)}" r="${f1(96*size+22)}" fill="url(#sunG${u})" opacity="${f3(.20*size*(1-colP*.7))}"/>`
        : '';

      /* ---- the jaw becomes the event horizon */
      const jaw=win(t,CUP+.24,OPEN+.24)*(1-warpP*.55);
      let mouth='M62 94 Q80 109 98 94', fangs='';
      if(jaw>0){mouth=`M56 92 Q80 ${f1(110+jaw*34)} 104 92 Z`;
        fangs=lsTeeth([[68,4.5],[92,4.5]],92,11,T().pal.line);}
      E.face.innerHTML=sticker(u,{eyeK:1.06+jaw*.42+hit*.14,mouth,fangs,glow:true,hornRot:Math.sin(t*3)*2+jaw*6});
      E.face.setAttribute('transform',
        `translate(${CX},${f1(CY-24-jaw*3)}) rotate(${f2(-win(t,1.05,2.8)*(1-warpP)*2)}) scale(${f3(intro)}) translate(-80,-80)`);

      /* ---- the screen is dragged into the hole */
      const w2=easeIn(warpP);
      E.suck.setAttribute('transform',
        `translate(${HX} ${HY}) scale(${f3(1-w2*.97)}) rotate(${f1(w2*26)}) translate(${-HX} ${-HY})`);
      E.suck.setAttribute('opacity',f3(1-w2*.92));
      /* the name is shredded by the vortex a beat before the hands slam shut */
      E.bname.setAttribute('opacity',f3(1-win(t,WARP,WARP+.22)));

      /* ---- ignition ring, then the clap */
      let shock='';
      if(t>=IGNITE&&t<IGNITE+.42){
        const p=(t-IGNITE)/.42;
        shock+=`<circle r="${f1(5+p*70)}" fill="none" stroke="${p<.45?'#ffffff':T().accent}" stroke-width="${f2(2.2*(1-p)+.25)}" opacity="${f3((1-p)*.6)} "/>`;
        shock+=streaks(t,p,12,10,72);
      }
      for(let k=0;k<3;k++){
        const p=win(t,CLAP+k*.09,CLAP+k*.09+.5);
        if(t>=CLAP+k*.09&&p<1)
          shock+=`<circle r="${f1(6+p*(120+k*16))}" fill="none" stroke="${k===0?'#ffffff':(k===1?T().accent:T().accent2)}" stroke-width="${f2(2.6*(1-p)+.3)}" opacity="${f3((1-p)*.75)}"/>`;
      }
      if(impact>=0&&impact<.30) shock+=debris(impact/.30,20);
      if(hit>.02) shock+=chroma(16+(1-hit)*54,1.7,hit*.6);
      E.shock.innerHTML=shock;

      E.flash.setAttribute('opacity',f3(t>=CLAP?.13*Math.max(0,1-impact/.16):0));
      sp.style.opacity=f3(1-win(t,FA,FB));
      for(let i=0;i<4;i++)sp.querySelectorAll('.pip')[i].classList.toggle('on',t>ORBS[i]+0.45);
    }};
  }
