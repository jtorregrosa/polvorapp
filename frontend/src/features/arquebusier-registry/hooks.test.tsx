import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import {
  getGetComplianceStatisticsQueryKey,
  getGetComplianceSummaryQueryKey,
} from '@/api/generated/compliance/compliance';
import {
  getGetArquebusierQueryKey,
  getListArquebusiersQueryKey,
} from '@/api/generated/arquebusiers/arquebusiers';
import { useRefreshArquebusier, useRefreshRegistryViews } from './hooks';

// Spec "Warning count in the navigation": after a change to an arquebusier, the navigation count,
// the dashboard and the statistics follow without reloading the page.

describe('useRefreshArquebusier', () => {
  it('invalidates the arquebusier, the list, the warning summary and every statistics query', async () => {
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useRefreshArquebusier('00000000-0000-4000-8000-000000000301'), {
      wrapper,
    });

    await result.current();

    const keys = invalidate.mock.calls.map(([filters]) => filters?.queryKey);
    expect(keys).toContainEqual(getGetArquebusierQueryKey('00000000-0000-4000-8000-000000000301'));
    expect(keys).toContainEqual(getListArquebusiersQueryKey());
    expect(keys).toContainEqual(getGetComplianceSummaryQueryKey());
    expect(keys).toContainEqual(getGetComplianceStatisticsQueryKey());
    queryClient.clear();
  });

  it('refetches statistics whatever their filters', async () => {
    const queryClient = new QueryClient();
    const filtered = getGetComplianceStatisticsQueryKey({
      comparsaId: '00000000-0000-4000-8000-000000000101',
      status: 'ACTIVE',
    });
    queryClient.setQueryData(filtered, { data: {} });
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useRefreshArquebusier('00000000-0000-4000-8000-000000000301'), {
      wrapper,
    });

    await result.current();

    expect(queryClient.getQueryState(filtered)?.isInvalidated).toBe(true);
    queryClient.clear();
  });
});

describe('useRefreshRegistryViews', () => {
  it('invalidates the list and the insights after a change made by someone else', () => {
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useRefreshRegistryViews(), { wrapper });

    result.current();

    const keys = invalidate.mock.calls.map(([filters]) => filters?.queryKey);
    expect(keys).toEqual(
      expect.arrayContaining([
        getListArquebusiersQueryKey(),
        getGetComplianceSummaryQueryKey(),
        getGetComplianceStatisticsQueryKey(),
      ]),
    );
    queryClient.clear();
  });
});
