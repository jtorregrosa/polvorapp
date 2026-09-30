import type { ParseKeys } from 'i18next';
import { Fragment } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useMatches } from 'react-router';
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from '@/components/ui/breadcrumb';

/** Route `handle` contract: routes declare their crumb as a `common` translation key. */
export interface RouteHandle {
  breadcrumb?: ParseKeys;
}

function isRouteHandle(handle: unknown): handle is RouteHandle {
  return typeof handle === 'object' && handle !== null && 'breadcrumb' in handle;
}

/**
 * Path from the start page to the current page, built from the matched routes' handles
 * (design D5). The last crumb is the current page.
 */
export function Breadcrumbs() {
  const { t } = useTranslation();
  const matches = useMatches();
  const crumbs = [
    { key: 'home', to: '/', label: t('nav.home') },
    ...matches.flatMap((match) => {
      const key = isRouteHandle(match.handle) ? match.handle.breadcrumb : undefined;
      return key ? [{ key: match.id, to: match.pathname, label: t(key) }] : [];
    }),
  ];

  return (
    <Breadcrumb>
      <BreadcrumbList>
        {crumbs.map((crumb, index) => (
          <Fragment key={crumb.key}>
            {index > 0 && <BreadcrumbSeparator />}
            <BreadcrumbItem>
              {index === crumbs.length - 1 ? (
                <BreadcrumbPage>{crumb.label}</BreadcrumbPage>
              ) : (
                <BreadcrumbLink asChild>
                  <Link to={crumb.to}>{crumb.label}</Link>
                </BreadcrumbLink>
              )}
            </BreadcrumbItem>
          </Fragment>
        ))}
      </BreadcrumbList>
    </Breadcrumb>
  );
}
