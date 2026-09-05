# Signature Profile Matrix

| Format | B | T | LT | LTA | MVP |
|---|---:|---:|---:|---:|---:|
| PAdES | Yes | Planned | Planned | Planned | B |
| XAdES | Yes | Planned | Planned | Planned | B |
| CAdES | Yes | Planned | Planned | Planned | B |
| ASiC-S | Planned | Planned | Planned | Planned | No |
| ASiC-E | Planned | Planned | Planned | Planned | No |

## B

Base signature with the required cryptographic and certificate attributes.

## T

B-level signature plus a trusted timestamp.

## LT

T-level signature plus the validation material required for long-term validation.

## LTA

LT-level signature plus archival preservation timestamps/evidence as required by the applicable profile.

The implementation must validate actual profile requirements instead of treating these levels as string labels.
