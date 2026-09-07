import type { ButtonHTMLAttributes, ReactNode } from "react";
import { Link } from "react-router-dom";
import { SpinnerIcon } from "./Icons";

export type ButtonVariant = "primary" | "ghost" | "panel" | "panelPrimary";
export type ButtonSize = "lg" | "md" | "sm";

interface CommonProps {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  loadingLabel?: string;
  className?: string;
  children: ReactNode;
}

type LinkProps = CommonProps & { to: string; href?: never; onClick?: never; disabled?: never; type?: never };
type AnchorProps = CommonProps & { href: string; to?: never; onClick?: never; disabled?: never; type?: never; external?: boolean };
type NativeProps = CommonProps & { to?: never; href?: never } & ButtonHTMLAttributes<HTMLButtonElement>;

export type ButtonProps = LinkProps | AnchorProps | NativeProps;

const SIZE: Record<ButtonSize, string> = {
  lg: "min-h-[54px] px-[26px] text-[16px]",
  md: "min-h-[46px] px-5 text-[15px]",
  sm: "min-h-10 px-4 text-[14px]",
};

const VARIANT: Record<ButtonVariant, string> = {
  primary:
    "bg-accent text-accent-ink border-accent hover:bg-[color-mix(in_oklab,var(--af-accent)_82%,#000)] hover:border-[color-mix(in_oklab,var(--af-accent)_82%,#000)] active:bg-[color-mix(in_oklab,var(--af-accent)_68%,#000)]",
  ghost:
    "bg-transparent text-ink border-ink hover:bg-[color-mix(in_oklab,var(--af-ink)_6%,transparent)] active:bg-[color-mix(in_oklab,var(--af-ink)_12%,transparent)]",
  panel:
    "bg-transparent text-panel-ink border-panel-line hover:bg-wash/8 hover:border-wash/30 active:bg-wash/14",
  panelPrimary:
    "bg-accent-panel text-panel-on-ink border-accent-panel hover:bg-[color-mix(in_oklab,var(--af-accent-panel)_84%,#fff)] hover:border-[color-mix(in_oklab,var(--af-accent-panel)_84%,#fff)] active:bg-[color-mix(in_oklab,var(--af-accent-panel)_72%,#000)]",
};

const DISABLED = "disabled:bg-line disabled:text-ink-3 disabled:border-line disabled:border-dashed disabled:cursor-not-allowed disabled:hover:bg-line";

export function Button(props: ButtonProps) {
  const { variant = "primary", size = "lg", loading = false, loadingLabel, className = "", children } = props;
  const classes = `inline-flex items-center justify-center gap-2.5 rounded-pill border font-semibold text-center whitespace-nowrap no-underline transition-colors ${SIZE[size]} ${VARIANT[variant]} ${className}`;
  const content = loading ? (
    <>
      <SpinnerIcon size={16} />
      {loadingLabel ?? children}
    </>
  ) : (
    children
  );

  if ("to" in props && props.to !== undefined) {
    return (
      <Link to={props.to} className={classes}>
        {content}
      </Link>
    );
  }
  if ("href" in props && props.href !== undefined) {
    const external = "external" in props && props.external;
    return (
      <a href={props.href} className={classes} target={external ? "_blank" : undefined} rel={external ? "noreferrer" : undefined}>
        {content}
      </a>
    );
  }
  const { variant: _v, size: _s, loading: _l, loadingLabel: _ll, className: _c, children: _ch, ...native } = props as NativeProps;
  return (
    <button type="button" {...native} disabled={native.disabled || loading} aria-busy={loading || undefined} className={`${classes} ${DISABLED}`}>
      {content}
    </button>
  );
}
