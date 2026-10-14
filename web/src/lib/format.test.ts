import { describe, expect, it } from 'vitest'
import { formatDate, formatQuantity, unitLabel } from './format'

describe('format', () => {
  it('formats quantities with unit abbreviations', () => {
    expect(formatQuantity(1.5, 'KG')).toBe('1,5 kg')
    expect(formatQuantity(null, 'G')).toBe('a gosto')
    expect(unitLabel('COLHER_SOPA')).toBe('col. sopa')
  })
  it('formats ISO dates without timezone shifts', () => {
    expect(formatDate('2026-03-09')).toBe('09/03/2026')
    expect(formatDate(null)).toBe('sem validade')
  })
})
