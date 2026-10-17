  /* ==========================================================================
     v5 — "Remastered": the black-hole boot screen, shot with a camera.

     The rig and the framing are the ones every other loading screen uses: the
     sticker sits dead centre on (CX, CY) at the same 0.9 scale the rune and
     drag screens use, so this variant is the same size and in the same place
     as its siblings. The hole is born just under his chin and the wordmark
     sits below the card, clear of it.

     The story is the one this variant has always told — one orb per saved-post
     pip spirals in, a filament ignites between the palms, the core punches
     open, he drags the disk wide, then claps and the frame is pulled in.

     The polish is deliberately screen-space, never world-space: the same
     `sp.style.transform` shake/zoom the shipped screen used (so nothing drifts
     off centre), a seeded star field, a little parallax dust, swirl lines on
     the disk, radial streaks on the ignition and an RGB-split ring plus debris
     on the clap.
     ========================================================================== */
  function v5(sp){
    const T1=6.8, u=++uid;
    const ORBS=[0.39,0.68,0.98,1.27], IGNITE=1.46, CUP=0.82, OPEN=1.40,
          SPIN=2.31, WARP=2.87, CLAP=3.55, COL=3.81, FA=3.55, FB=4.00;

    /* ---- framing: identical to v1..v4 -------------------------------------
       the card is centred on (CX, CY) at 0.9, exactly like the rune screen's
       `translate(CX,CY) scale(0.92)` and the trident screen's 0.86. */
    const CARD=0.95;                  /* same as v2 / v4                      */
    const HX=CX, HY=CY+14;            /* the hole, born at the mouth           */
    const BRAND_Y=CY+95;              /* the name, below the card (v2)        */
    const NP=28;
    const f1=n=>n.toFixed(1), f2=n=>n.toFixed(2), f3=n=>n.toFixed(3);
    const lerp=(a,b,k)=>a+(b-a)*k;
    const easeIn=k=>k*k*k;
    const backK=k=>{const c=1.9;return 1+(c+1)*Math.pow(k-1,3)+c*Math.pow(k-1,2)};
    const TAU2=Math.PI*2;

    /* same map the gallery uses: a point of the card's 160x160 art, on screen */
    function cardPoint(vx,vy,scale,rot){
      const px=(vx-80)*scale, py=(vy-80)*scale, r=rot*Math.PI/180;
      return {x:CX+px*Math.cos(r)-py*Math.sin(r), y:CY+px*Math.sin(r)+py*Math.cos(r)};
    }

    /* deterministic scatter - the same boot always looks alike */
    function scatter(n,seed,box){
      let s=seed>>>0, out=[];
      const rnd=()=>((s=(s*1664525+1013904223)>>>0)/4294967296);
      for(let i=0;i<n;i++) out.push({x:(rnd()-.5)*box.w, y:(rnd()-.5)*box.h, r:.28+rnd()*.55, a:.14+rnd()*.34, ph:rnd()*TAU2});
      return out;
    }
    const BX=W*1.4, BY=H*1.4;
    const SKY=scatter(26,1337,{w:BX,h:BY});
    const FDUST=scatter(8,2027,{w:BX,h:BY});
    const NDUST=scatter(6,7717,{w:BX,h:BY});
    const skyMarkup=SKY.map(s=>`<circle cx="${f1(s.x)}" cy="${f1(s.y)}" r="${f2(s.r)}" fill="#d8f1ff" opacity="${f3(s.a)}"/>`).join('');

    function dust(pts,t,pull,op,soft){
      let s='';
      for(const p of pts){
        const a=Math.atan2(p.y,p.x)+t*.10+pull*1.9;
        const d=Math.hypot(p.x,p.y)*(1-pull*(.42+.5*p.r));
        const x=Math.cos(a)*d+Math.sin(t*.6+p.ph)*3;
        const y=Math.sin(a)*d*.92+Math.cos(t*.45+p.ph)*2.4;
        s+=`<circle cx="${f1(x)}" cy="${f1(y)}" r="${f2(p.r*soft)}" fill="url(#mote${u})" opacity="${f3(p.a*op*(1-pull*.35))}"/>`;
      }
      return s;
    }
    /* swirl around the hole - local to the hole, never across the frame */
    function wind(t,k,n,rIn,rOut){
      if(k<=.01) return '';
      let s='';
      for(let i=0;i<n;i++){
        const a=(i/n)*TAU2+t*(.5+(i%5)*.14);
        const rr=lerp(rIn,rOut,((i*37)%100)/100);
        const len=(3.4+(i*13)%11)*k/rr;
        const x1=Math.cos(a)*rr, y1=Math.sin(a)*rr*.62;
        const x2=Math.cos(a+len)*rr, y2=Math.sin(a+len)*rr*.62;
        s+=`<path d="M ${f1(x1)} ${f1(y1)} A ${f1(rr)} ${f1(rr*.62)} 0 0 1 ${f1(x2)} ${f1(y2)}" fill="none" stroke="${i%3?T().accent:T().accent2}" stroke-width="${f2(.4+(i%3)*.28)}" stroke-linecap="round" opacity="${f3(k*(.16+.42*((i*7)%10)/10))}"/>`;
      }
      return s;
    }
    function streaks(p,n,r0,r1){
      if(p<=.01||p>=1) return '';
      let s='';
      for(let i=0;i<n;i++){
        const a=(i/n)*TAU2+.19+Math.sin(i*4.7)*.06;
        const k=((i*29)%100)/100;
        const inner=r0+(r1-r0)*(.30+.50*k)*(1-p*.45);
        const outer=inner+(4+14*k)*(1-p);
        s+=`<line x1="${f1(Math.cos(a)*inner)}" y1="${f1(Math.sin(a)*inner*.78)}" x2="${f1(Math.cos(a)*outer)}" y2="${f1(Math.sin(a)*outer*.78)}" stroke="${i%3===0?'#ffffff':(i%2?T().accent:T().accent2)}" stroke-width="${f2(.7+k*.7)}" stroke-linecap="round" opacity="${f3((1-p)*(.18+.42*k))}"/>`;
      }
      return s;
    }
    function debris(p,n){
      if(p<=0||p>=1) return '';
      let s='';
      for(let i=0;i<n;i++){
        const a=(i*2.399963)%TAU2, sp2=.55+((i*17)%23)/23*.95, d=p*(24+104*sp2);
        s+=`<circle cx="${f1(Math.cos(a)*d)}" cy="${f1(Math.sin(a)*d*.78)}" r="${f2(Math.max(.15,(1-p)*(.8+(i%3)*.55)))}" fill="${i%3?T().accent:T().accent2}" opacity="${f3((1-p)*.8)}"/>`;
      }
      return s;
    }
    function chroma(r,sw,op){
      if(op<=.01||r<=1) return '';
      return `<g opacity="${f3(op)}"><circle cx="-1.6" r="${f1(r)}" fill="none" stroke="#ff2d7a" stroke-width="${f2(sw)}" opacity=".85"/><circle cx="1.6" r="${f1(r)}" fill="none" stroke="#20d9ff" stroke-width="${f2(sw)}" opacity=".85"/></g>`;
    }

    let orbs='',parts='',disk='';
    for(let i=0;i<4;i++){
      const c=i%2?T().accent2:T().accent;
      orbs+=`<g id="ob${i}${u}"><circle r="4" fill="${c}"/><circle r="7" fill="none" stroke="${c}" stroke-width="1" opacity=".55"/><circle r="10.5" fill="none" stroke="${c}" stroke-width=".7" opacity=".22"/></g>`;
    }
    for(let i=0;i<NP;i++)parts+=`<line id="pt${i}${u}" x1="0" y1="0" x2="0" y2="0" stroke="${i%3?T().accent:T().accent2}" stroke-width="1.1" stroke-linecap="round" opacity="0"/>`;
    [[26,8,'2.3','10 6'],[42,12,'2','13 8'],[61,17,'1.6','18 11'],[82,24,'1.2','24 15']]
      .forEach((d,i)=>{disk+=`<ellipse id="dk${i}${u}" rx="${d[0]}" ry="${d[1]}" fill="none" stroke="url(#dsk${u})" stroke-width="${d[2]}" stroke-dasharray="${d[3]}" opacity="0"/>`;});

    try{ sp.setAttribute('data-ls-cut','v5-remastered-2'); }catch(_){}
    sp.innerHTML=`<svg viewBox="0 0 ${W} ${H}" preserveAspectRatio="xMidYMid meet">
      ${glowDef(u)}${vignetteDef(u)}${stickerParts(u,{}).defs}
      <defs>
        <radialGradient id="coreG${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#000" stop-opacity="1"/>
          <stop offset="72%" stop-color="#000" stop-opacity="1"/>
          <stop offset="88%" stop-color="#000" stop-opacity=".9"/>
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
          <stop offset="0%" stop-color="#eaf9ff" stop-opacity=".9"/>
          <stop offset="46%" stop-color="#cfefff" stop-opacity=".32"/>
          <stop offset="100%" stop-color="#bfe6ff" stop-opacity="0"/></radialGradient>
        <radialGradient id="wash${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="${T().accent}" stop-opacity=".26"/>
          <stop offset="46%" stop-color="${T().accent}" stop-opacity=".09"/>
          <stop offset="74%" stop-color="${T().accent2}" stop-opacity=".06"/>
          <stop offset="100%" stop-color="${T().accent2}" stop-opacity="0"/></radialGradient>
        <radialGradient id="sunG${u}" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#ffffff" stop-opacity=".85"/>
          <stop offset="22%" stop-color="${T().accent}" stop-opacity=".28"/>
          <stop offset="55%" stop-color="${T().accent}" stop-opacity=".08"/>
          <stop offset="78%" stop-color="${T().accent2}" stop-opacity=".04"/>
          <stop offset="100%" stop-color="${T().accent2}" stop-opacity="0"/></radialGradient>
      </defs>
      <rect width="${W}" height="${H}" fill="${T().solid}"/>
      <rect width="${W}" height="${H}" fill="url(#vig${u})"/>
      <g id="sky${u}">${skyMarkup}</g>
      <circle id="washEl${u}" cx="${HX}" cy="${HY}" r="0" fill="url(#wash${u})" opacity="0"/>
      <g id="farDust${u}"></g>
      <g id="orbs${u}" transform="translate(${HX},${HY})">${orbs}</g>
      <g id="suck${u}">
        <g id="face${u}" transform="translate(${CX},${CY}) scale(${CARD}) translate(-80,-80)"></g>
        <g id="bname${u}">${brandName(u,CX,BRAND_Y,14)}</g>
      </g>
      <g id="parts${u}" transform="translate(${HX},${HY})">${parts}</g>
      <g id="hole${u}" transform="translate(${HX},${HY})">
        <circle id="halo${u}" r="0" fill="url(#haloG${u})" opacity="0" filter="url(#glow${u})"/>
        <g id="disk${u}" transform="rotate(-17)" filter="url(#glow${u})">${disk}
          <ellipse id="dkb${u}" rx="18" ry="5.5" fill="none" stroke="#ffffff" stroke-width="2.8" opacity="0"/></g>
        <circle id="coreC${u}" r="0" fill="url(#coreG${u})"/>
        <g id="lens${u}" opacity="0">
          <path d="M -27 -3 A 27 27 0 0 1 27 -3" fill="none" stroke="url(#dsk${u})" stroke-width="2.2"/>
          <path d="M -27 3 A 27 27 0 0 0 27 3" fill="none" stroke="url(#dsk${u})" stroke-width="2.2"/>
        </g>
        <circle id="ein${u}" r="0" fill="none" stroke="url(#dsk${u})" stroke-width="1.7" opacity="0"/>
        <circle id="rim${u}" r="0" fill="none" stroke="#ffffff" stroke-width="1.1" opacity="0"/>
      </g>
      <g id="windEl${u}" transform="translate(${HX},${HY})"></g>
      <g id="arms${u}"></g>
      <g id="lightFront${u}"></g>
      <g id="nearDust${u}"></g>
      <g id="shock${u}" transform="translate(${HX},${HY})"></g>
      <rect id="flash${u}" width="${W}" height="${H}" fill="${T().accent}" opacity="0"/>
    </svg>`+CAP;

    const q=i=>sp.querySelector('#'+i+u);
    const E={sky:q('sky'),wash:q('washEl'),farDust:q('farDust'),nearDust:q('nearDust'),
             orbs:[0,1,2,3].map(i=>q('ob'+i)),suck:q('suck'),face:q('face'),bname:q('bname'),
             pt:[...Array(NP)].map((_,i)=>q('pt'+i)),dk:[0,1,2,3].map(i=>q('dk'+i)),dkb:q('dkb'),
             core:q('coreC'),halo:q('halo'),disk:q('disk'),lens:q('lens'),ein:q('ein'),rim:q('rim'),
             wind:q('windEl'),arms:q('arms'),lightFront:q('lightFront'),
             shock:q('shock'),flash:q('flash')};

    return {T:T1,update(t){
      const enter=win(t,0.02,0.40), intro=backK(enter);
      const cup=win(t,CUP,OPEN), openP=win(t,OPEN,SPIN), spinP=win(t,SPIN,WARP), warpP=win(t,WARP,CLAP);
      const clap=win(t,CLAP-0.18,CLAP), colP=win(t,CLAP,COL+0.06)*0.86;
      const impact=t-CLAP, hit=Math.max(0,1-Math.abs(impact)/0.30);

      /* the hole does not exist until the filament lights it, then it only
         ever grows - no plateau, no black dot sitting on his face at t=0 */
      const grow=.96*(openP*.40+spinP*.32+warpP*.28);
      const bloom=win(t,IGNITE+.10,IGNITE+.50);   /* it is born after the spark */
      const size=Math.max(0,Math.min(1,Math.pow(grow,1.06)*bloom*(1-colP)));

      /* ---- sky, key light, drifting dust (all behind the card) */
      E.sky.setAttribute('opacity',f3(.7+.3*size));
      E.wash.setAttribute('r',f1(96+70*size));
      E.wash.setAttribute('opacity',f3(.26*Math.max(0,1-Math.abs(t-IGNITE)/.5)+.42*size));
      E.farDust.innerHTML=dust(FDUST,t,warpP*.55,.42,1.9);
      E.nearDust.innerHTML=dust(NDUST,t,warpP*.95,.34,1.3);

      /* ---- four orbs, one per boot step, each dragging a short light trail */
      E.orbs.forEach((el,i)=>{
        const p=(t-ORBS[i])/.86;
        if(p<0||p>1){el.setAttribute('opacity','0');return;}
        const ang=i*1.7+p*7.4+t*.10, rr=lerp(104,3,easeIn(p));
        el.setAttribute('transform',`translate(${f1(Math.cos(ang)*rr)},${f1(Math.sin(ang)*rr*.60)}) scale(${f2(1-p*.72)})`);
        el.setAttribute('opacity',f3(Math.min(1,p*6)*(1-p*p)));
      });

      /* ---- accretion disk */
      E.halo.setAttribute('r',f1(92*size));
      E.halo.setAttribute('opacity',f3(Math.pow(size,1.2)*(.82+.18*Math.sin(t*15))));
      E.disk.setAttribute('transform',`rotate(${f1(-17+t*46)})`);
      E.dk.forEach((el,i)=>{
        const on=win(t,OPEN+.08+i*.10,OPEN+.42+i*.10);
        el.setAttribute('opacity',f3(on*(.94+.06*Math.sin(t*12+i))*(1-colP)));
        el.setAttribute('transform',`scale(${f3(.22+Math.min(1,size)*.80)})`);
        el.setAttribute('stroke-dashoffset',f1(-((t*92*(1+i*.38))%240)));
      });
      E.dkb.setAttribute('opacity',f3(Math.pow(size,1.2)*(.82+.18*Math.sin(t*18))));
      E.dkb.setAttribute('stroke-width',f2(2.4+1.4*size));
      E.dkb.setAttribute('transform',`scale(${f3(.26+size*.80)})`);
      E.core.setAttribute('r',f1(56*size));
      E.lens.setAttribute('opacity',f3(size*(.82+.18*Math.sin(t*11))));
      E.lens.setAttribute('transform',`scale(${f3((56*size+3)/27)}) rotate(${f1(t*24)})`);
      E.ein.setAttribute('r',f1(64*size));
      E.ein.setAttribute('stroke-width',f2(1.8+1.3*size*(.55+.45*Math.sin(t*13))));
      E.ein.setAttribute('opacity',f3(.94*Math.min(1,size*2.2)));
      E.rim.setAttribute('r',f1(56.5*size));
      E.rim.setAttribute('stroke-width',f2(1.1+.5*size));
      E.rim.setAttribute('opacity',f3(Math.min(1,size*2.2)*(.55+.35*Math.sin(t*17))));

      /* ---- matter spiralling in */
      E.pt.forEach((el,i)=>{
        const on=win(t,OPEN-.2,OPEN+.34);
        const ph=((t*.88)+(i/NP))%1, pp=ph*ph;
        const ang=(i/NP)*Math.PI*2+pp*10.2+t*.10;
        const rr=lerp(78,3,pp), tail=1.22+.22*size;
        el.setAttribute('x1',f1(Math.cos(ang)*rr));el.setAttribute('y1',f1(Math.sin(ang)*rr*.52));
        el.setAttribute('x2',f1(Math.cos(ang-.25)*rr*tail));el.setAttribute('y2',f1(Math.sin(ang-.25)*rr*tail*.52));
        el.setAttribute('stroke-width',f2(.9+size*.7+(1-ph)*.3));
        el.setAttribute('opacity',f3(on*(1-colP)*Math.min(1,ph*5)*(1-pp*.3)*(.6+size*.4)));
      });

      /* ---- swirl, tight to the disk */
      E.wind.innerHTML=wind(t,Math.min(1,spinP*.55+warpP),12,26,58);

      /* ---- HE MAKES IT WITH HIS HANDS: the arms rise out of the card's own
         bottom corners, the palms cup nothing, the core lights between them,
         he drags it wide, then slams his hands together and it eats the frame. */
      const cardScale=intro*CARD;
      const enterArm=win(t,.26,.92);
      /* the palms hold the rim, so they slide out as the hole is dragged wide */
      const rimR=56*size;
      let gap=lerp(66,Math.max(60,rimR+22),win(t,CUP,OPEN+.35));
      gap=lerp(gap,Math.max(66,rimR+26),spinP);
      gap=lerp(gap,6,clap);
      const trem=Math.sin(t*15)*2.2*cup*(1-clap)+Math.sin(t*41)*.55*spinP;
      const hy=HY+2+2*cup-2*spinP+trem*.5;
      const hOp=enterArm*(1-Math.pow(warpP,3));
      /* imp-outer-frame is x14 y14 w132 h132: its bottom corners are 14,146 / 146,146 */
      const shL=cardPoint(14,146,cardScale,0), shR=cardPoint(146,146,cardScale,0);
      E.arms.innerHTML=hOp>.02
        ? armToHandAimed(u,shL.x,shL.y,HX-gap,hy,HX,HY,true ,hOp)
         +armToHandAimed(u,shR.x,shR.y,HX+gap,hy,HX,HY,false,hOp)
        : '';

      /* ---- the filament between the palms before the core lights */
      if(cup>.18&&hOp>.02&&t<1.72){
        const spark=win(t,1.02,1.18)*(1-win(t,1.32,1.48));
        if(spark>.02){
          E.arms.innerHTML+=`<g opacity="${f3(spark)}" filter="url(#glow${u})">
            <line x1="${f1(HX-gap+7)}" y1="${f1(hy)}" x2="${f1(HX+gap-7)}" y2="${f1(hy)}" stroke="#ffffff" stroke-width="1" stroke-dasharray="2 5" stroke-dashoffset="${f1(-t*70)}"/>
            <line x1="${f1(HX-gap+7)}" y1="${f1(hy-3)}" x2="${f1(HX+gap-7)}" y2="${f1(hy+3)}" stroke="${T().accent}" stroke-width=".7" stroke-dasharray="1 8" stroke-dashoffset="${f1(t*50)}" opacity=".8"/>
            <circle cx="${f1(HX)}" cy="${f1(hy)}" r="${f2(Math.max(.4,1.2+1.4*Math.sin(t*34)))}" fill="#ffffff"/></g>`;
        }
      }

      /* ---- the hole washes light over the palms and the chin */
      E.lightFront.innerHTML=size>.03
        ? `<circle cx="${f1(HX)}" cy="${f1(HY)}" r="${f1(78*size+20)}" fill="url(#sunG${u})" opacity="${f3(.20*size*(1-colP*.7))}"/>`
        : '';

      /* ---- the jaw becomes the event horizon */
      const jaw=win(t,OPEN+.25,SPIN+.10)*(1-warpP*.55);
      let mouth='M62 94 Q80 109 98 94', fangs='';
      if(jaw>0){mouth=`M56 92 Q80 ${f1(110+jaw*34)} 104 92 Z`;
        if(jaw>.34) fangs=lsTeeth([[68,4.5],[92,4.5]],92,11,T().pal.line);}
      E.face.innerHTML=sticker(u,{eyeK:1.04+jaw*.34+hit*.12,mouth,fangs,glow:true,hornRot:Math.sin(t*3)*2+jaw*6});
      E.face.setAttribute('transform',`translate(${CX},${CY}) scale(${f3(cardScale)}) translate(-80,-80)`);

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
        shock+=`<circle r="${f1(5+p*62)}" fill="none" stroke="${p<.45?'#ffffff':T().accent}" stroke-width="${f2(2*(1-p)+.25)}" opacity="${f3((1-p)*.6)}"/>`;
        shock+=streaks(p,9,20,52);
      }
      for(let k=0;k<3;k++){
        const p=win(t,CLAP+k*.09,CLAP+k*.09+.5);
        if(t>=CLAP+k*.09&&p<1)
          shock+=`<circle r="${f1(6+p*(96+k*14))}" fill="none" stroke="${k===0?'#ffffff':(k===1?T().accent:T().accent2)}" stroke-width="${f2(2.6*(1-p)+.3)}" opacity="${f3((1-p)*.75)}"/>`;
      }
      if(impact>=0&&impact<.30) shock+=debris(impact/.30,18);
      if(hit>.02) shock+=chroma(14+(1-hit)*40,1.5,hit*.6);
      E.shock.innerHTML=shock;

      E.flash.setAttribute('opacity',f3(t>=CLAP?.13*Math.max(0,1-impact/.16):0));

      /* ---- shake and punch exactly as the shipped screen does it: screen
         space around the stage's own centre, so the card never drifts */
      const hitZoom=impact>=0?.05*Math.max(0,1-impact/.34):0;
      const shakeEnv=Math.max(0,1-Math.abs(t-(CLAP+.015))/.24);
      const shakeX=(Math.sin(t*91)+0.48*Math.sin(t*147+1.2))*shakeEnv*3.0;
      const shakeY=(Math.cos(t*103+0.6)+0.38*Math.sin(t*169))*shakeEnv*2.4;
      sp.style.transformOrigin='50% 50%';
      sp.style.transform=`translate(${shakeX.toFixed(2)}px,${shakeY.toFixed(2)}px) scale(${(1+0.022*spinP+hitZoom).toFixed(3)})`;

      sp.style.opacity=f3(1-win(t,FA,FB));
      for(let i=0;i<4;i++)sp.querySelectorAll('.pip')[i].classList.toggle('on',t>ORBS[i]+0.45);
    }};
  }
