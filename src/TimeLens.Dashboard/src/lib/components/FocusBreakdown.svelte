<script lang="ts">
  import type { CategoryEntry } from '../types';
  import { fmtPrecise } from '../utils';
  import { showSeconds } from '../stores/settings';

  let { categories, score, activeSeconds }: { categories: CategoryEntry[]; score: number; activeSeconds: number } = $props();

  const focusedNames = new Set(['development', 'work', 'documents', 'communication', 'design']);
  const focusedSeconds = $derived(categories
    .filter(item => focusedNames.has(item.name.toLowerCase()))
    .reduce((sum, item) => sum + item.minutes * 60, 0));
  const unclassifiedSeconds = $derived(categories
    .filter(item => item.name.toLowerCase() === 'other')
    .reduce((sum, item) => sum + item.minutes * 60, 0));
  const classifiedSeconds = $derived(Math.max(0, activeSeconds - unclassifiedSeconds));
  const remainingSeconds = $derived(Math.max(0, classifiedSeconds - focusedSeconds));

  function width(seconds: number, total = activeSeconds): number {
    return total > 0 ? Math.min(100, Math.max(0, seconds / total * 100)) : 0;
  }
</script>

<section class="focus-card" aria-labelledby="focus-breakdown-title">
  <header>
    <div class="heading"><i class="ti ti-target-arrow" aria-hidden="true"></i><div><h2 id="focus-breakdown-title">Focus breakdown</h2><p>How the score is composed</p></div></div>
    <span class="score" title="Focused category time divided by classified active time">{score}<small>/100</small></span>
  </header>

  <div class="focus-meter" aria-label={`Focus score ${score} out of 100`}><span style={`width:${score}%`}></span></div>
  <div class="breakdown">
    <div class="metric">
      <div><span class="dot focused"></span><strong>Focused categories</strong><time>{fmtPrecise(focusedSeconds, $showSeconds)}</time></div>
      <span class="track"><i class="focused" style={`width:${width(focusedSeconds)}%`}></i></span>
    </div>
    <div class="metric">
      <div><span class="dot remaining"></span><strong>Other classified</strong><time>{fmtPrecise(remainingSeconds, $showSeconds)}</time></div>
      <span class="track"><i class="remaining" style={`width:${width(remainingSeconds)}%`}></i></span>
    </div>
    {#if unclassifiedSeconds > 0}
      <div class="metric compact">
        <div><span class="dot unclassified"></span><strong>Unclassified</strong><time>{fmtPrecise(unclassifiedSeconds, $showSeconds)}</time></div>
        <span class="track"><i class="unclassified" style={`width:${width(unclassifiedSeconds)}%`}></i></span>
      </div>
    {/if}
  </div>
  <p class="formula"><i class="ti ti-info-circle" aria-hidden="true"></i><span><b>Score = focused ÷ classified active time.</b> Development, work, documents, communication, and design count as focused. “Other” is excluded.</span></p>
</section>

<style>
  .focus-card{min-width:0;min-height:225px;display:flex;flex-direction:column;padding:14px 16px 12px;border:1px solid var(--md-outline);border-radius:var(--shape-lg);background:var(--md-surface-1)}
  header,.heading,.heading>div,.metric>div,.formula{display:flex;align-items:center}.heading{min-width:0;gap:9px}.heading>i{color:var(--md-primary);font-size:17px}.heading>div{min-width:0;align-items:flex-start;flex-direction:column;gap:2px}h2{color:var(--clr-text-pri);font-size:13px;font-weight:650}.heading p{color:var(--clr-text-ter);font-size:10px}.score{margin-left:auto;color:var(--md-primary);font:700 25px/1 var(--font-mono)}.score small{color:var(--clr-text-ter);font-size:9px;font-weight:500}
  .focus-meter{height:8px;margin:15px 0 13px;overflow:hidden;border:1px solid color-mix(in srgb,var(--md-primary) 12%,var(--clr-border));border-radius:99px;background:color-mix(in srgb,var(--clr-bg-ter) 82%,var(--clr-bg-sec))}.focus-meter span{display:block;height:100%;min-width:2px;border-radius:inherit;background:linear-gradient(90deg,var(--md-secondary),var(--md-primary));box-shadow:0 0 12px color-mix(in srgb,var(--md-primary) 28%,transparent);transition:width var(--duration-base) var(--ease-out)}
  .breakdown{display:grid;gap:10px}.metric{display:grid;gap:5px}.metric>div{gap:7px}.metric strong{min-width:0;flex:1;color:var(--clr-text-sec);font-size:10px;font-weight:600}.metric time{color:var(--clr-text-pri);font:10px var(--font-mono)}.dot{width:7px;height:7px;flex:none;border-radius:50%}.focused{background:var(--md-primary)}.remaining{background:var(--md-secondary)}.unclassified{background:color-mix(in srgb,var(--md-on-surf-var) 72%,var(--md-primary))}.track{height:4px;overflow:hidden;border-radius:99px;background:color-mix(in srgb,var(--clr-border) 76%,var(--clr-bg-ter))}.track i{display:block;height:100%;min-width:2px;border-radius:inherit;transition:width var(--duration-base) var(--ease-out)}
  .formula{gap:7px;margin-top:auto;padding-top:11px;color:var(--clr-text-ter);font-size:9px;line-height:1.35}.formula i{flex:none;color:var(--md-primary);font-size:13px}.formula b{color:var(--clr-text-sec);font-weight:600}
  @media(max-width:760px){.focus-card{min-height:0}.formula{margin-top:10px}}
</style>
