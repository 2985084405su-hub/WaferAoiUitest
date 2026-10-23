import type { ReactNode } from 'react';

interface BannerProps {
  tone?: 'info' | 'warn' | 'readonly';
  children: ReactNode;
}

/** 常驻提示条：不用自动消失，状态改变时才移除。 */
export function Banner({ tone = 'info', children }: BannerProps) {
  return <p className={`banner banner-${tone}`}>{children}</p>;
}
