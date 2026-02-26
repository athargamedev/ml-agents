# LLM Dialogue Automation Checklist

Use this checklist before merging any dialogue or effect automation changes.

## Prompt & Schema

- [ ] Response schema is fixed and minimal (text + effect keys only)
- [ ] Effect keys map to registry entries (no free-form strings)
- [ ] System prompt includes allowed effects list

## Runtime Safety

- [ ] Server-authoritative dispatch via `NetworkDialogueService`
- [ ] Client only receives via ClientRpc
- [ ] Effect target resolution validates required components

## Automation Safety

- [ ] Structured edits used (insert/replace/delete)
- [ ] `validate_script(level:"standard")` passes
- [ ] Idempotent edits return `no_op` on repeat
- [ ] Stale SHA handling is implemented with a single retry

## Effect Quality

- [ ] Cooldown and rate limits enforced
- [ ] Audit log records effect key, target, time, status
- [ ] Effect failure reasons are logged and visible

## Verification

- [ ] EditMode test covers LLM response -> effect dispatch
- [ ] Console free of errors after automation run
